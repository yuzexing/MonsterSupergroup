using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class NetworkEvidenceTests
    {
        private sealed class Sink : IDiagnosticSink
        {
            public readonly List<DiagnosticRecord> Records = new();
            public bool TryWrite(DiagnosticRecord record) { Records.Add(record); return true; }
            public string RegisterEngine(object engine, string domain, Func<object, object> capture) => domain + ".test";
        }
        private IDiagnosticSink previous;
        private Sink sink;
        private Scene originalScene, testScene;
        [SetUp] public void SetUp()
        {
            previous = CombatEvidence.Sink; CombatEvidence.Sink = sink = new Sink();
            originalScene = SceneManager.GetActiveScene();
            testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(testScene);
        }
        [TearDown] public void TearDown()
        {
            CombatEvidence.Sink = previous;
            if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
            if (testScene.IsValid() && testScene.isLoaded) EditorSceneManager.CloseScene(testScene, true);
        }

        [Test] public void BatchedMessagesRetainPayloadIdentityWithoutKeepingPayloads()
        {
            byte[] input = { 9, 1, 2, 3, 9 };
            var command = new CommandMessage { netId = 15, componentIndex = 2, functionHash = 127, payload = new ArraySegment<byte>(input, 1, 3) };
            var rpc = new RpcMessage { netId = 18, componentIndex = 4, functionHash = 222, payload = new ArraySegment<byte>(new byte[] { 5, 6 }) };
            var expected = NetworkMessageEvidence.Describe(command);
            var batcher = new Batcher(1200);
            using var message = NetworkWriterPool.Get();
            NetworkMessages.Pack(command, message); batcher.AddMessage(message.ToArraySegment(), 10.25);
            message.Reset(); NetworkMessages.Pack(rpc, message); batcher.AddMessage(message.ToArraySegment(), 10.25);
            using var batch = NetworkWriterPool.Get();
            Assert.That(batcher.GetBatch(batch), Is.True);
            var members = NetworkMessageEvidence.DescribeBatch(batch.ToArraySegment(), out double time, out bool complete);
            Assert.That(complete, Is.True); Assert.That(time, Is.EqualTo(10.25)); Assert.That(members.Length, Is.EqualTo(2));
            Assert.That(members[0].payloadHash, Is.EqualTo(expected.payloadHash));
            Assert.That(members[0].entity, Is.EqualTo(15)); Assert.That(members[0].function, Is.EqualTo(127));
            Assert.That(members[1].kind, Is.EqualTo("Rpc"));
            input[1] = 100;
            Assert.That(NetworkMessageEvidence.Describe(command).payloadHash, Is.Not.EqualTo(expected.payloadHash));
            Assert.That(members[0].payloadHash, Is.EqualTo(expected.payloadHash));
        }

        [Test] public void TruncatedTransportBatchIsNotReportedAsComplete()
        {
            using var writer = NetworkWriterPool.Get(); writer.WriteDouble(1);
            Compression.CompressVarUInt(writer, 100); writer.WriteByte(2);
            Assert.Throws<InvalidDataException>(() => NetworkMessageEvidence.DescribeBatch(writer.ToArraySegment(), out _, out _));
        }

        [Test] public void OldOwnerAndOldEpochHaveDistinctEvidenceWithAssignmentHistory()
        {
            var registry = new ServerEnemySimulationRegistry(); registry.RegisterEnemy(100, Vector2.zero, 0);
            var first = registry.AssignClientOwner(100, 1, 1);
            var second = registry.AssignClientOwner(100, 2, 2);
            var snapshot = new EnemySimulationSnapshot { EnemyEntityId = 100, AssignmentEpoch = first.Epoch, Sequence = 1, SampleNetworkTime = 1 };
            Assert.That(registry.TryAcceptClientSnapshot(1, snapshot), Is.EqualTo(EnemySnapshotRejectionReason.WrongOwner));
            Assert.That(registry.TryAcceptClientSnapshot(2, snapshot), Is.EqualTo(EnemySnapshotRejectionReason.WrongEpoch));
            var decisions = sink.Records.Where(r => r.stage == "authority.movement").ToArray();
            Assert.That(decisions.Select(r => r.reason), Is.EqualTo(new[] { "WrongOwner", "WrongEpoch" }));
            Assert.That(decisions.All(r => r.target == 100 && r.assignmentEpoch == first.Epoch), Is.True);
            var handoff = sink.Records.Last(r => r.stage == "authority.assignment");
            Assert.That(((EnemySimulationAssignment)handoff.before).Epoch, Is.EqualTo(first.Epoch));
            Assert.That(((EnemySimulationAssignment)handoff.after).Epoch, Is.EqualTo(second.Epoch));
            Assert.That(registry.TryGetAssignment(100, out var current), Is.True);
            Assert.That(current.SimulationOwnerPlayerId, Is.EqualTo(2));
        }

        [Test] public void ReplicaMovementExplainsStaleSequenceAndTimestampSeparately()
        {
            var buffer = new EnemySnapshotBuffer();
            var snapshot = new EnemySimulationSnapshot { EnemyEntityId = 100, AssignmentEpoch = 2, Sequence = 5, SampleNetworkTime = 1 };
            Assert.That(buffer.Push(snapshot), Is.True);
            Assert.That(buffer.Push(snapshot, out var repeated), Is.False);
            Assert.That(repeated, Is.EqualTo(EnemySnapshotRejectionReason.StaleSequence));
            snapshot.Sequence = 6;
            Assert.That(buffer.Push(snapshot, out var oldTime), Is.False);
            Assert.That(oldTime, Is.EqualTo(EnemySnapshotRejectionReason.StaleTimestamp));
            snapshot.SampleNetworkTime = 2; snapshot.AssignmentEpoch = 1;
            Assert.That(buffer.Push(snapshot, out var oldEpoch), Is.False);
            Assert.That(oldEpoch, Is.EqualTo(EnemySnapshotRejectionReason.WrongEpoch));
        }

        [Test] public void PositiveHealthAfterPredictedDeathExplainsRetainedLocalState()
        {
            var obj = new GameObject("Canonical health evidence");
            try
            {
                var actor = obj.AddComponent<CombatantBehaviour>(); actor.Initialize(100); actor.ConfigureEntityId(100);
                actor.ConfigureKillConfirmation(true); actor.ConfigureClientFinalDeath(true);
                actor.ReceiveDamage(new DamageInfo(77, 100, false)); actor.ReceivePredictedLethalHit(default);
                Assert.That(actor.ApplyCanonicalHealth(90, 100, 2), Is.True);
                Assert.That(actor.CurrentHealth, Is.Zero);
                Assert.That(sink.Records.Last(r => r.stage == "entity.canonical_health").reason, Is.EqualTo("PredictedDeathRetained"));
                Assert.That(actor.ApplyCanonicalHealth(70, 100, 1), Is.False);
                Assert.That(sink.Records.Last(r => r.stage == "entity.canonical_health").reason, Is.EqualTo("StaleStateVersion"));
            }
            finally { UnityEngine.Object.DestroyImmediate(obj); }
        }

        [Test] public void PermissionEvidenceDistinguishesIndependentInvulnerabilitySources()
        {
            var obj = new GameObject("Independent permission evidence");
            try
            {
                var actor = obj.AddComponent<CombatantBehaviour>(); actor.ConfigureEntityId(12);
                actor.SetUpgradeSelectionInvulnerable(true); actor.SetUltimateInvulnerable(true);
                actor.SetUpgradeSelectionInvulnerable(false); actor.SetUpgradeSelectionInvulnerable(false);
                Assert.That(actor.IsInvulnerable, Is.True);
                var changes = sink.Records.Where(r => r.stage == "entity.permission").ToArray();
                Assert.That(changes.Select(r => r.reason), Is.EqualTo(new[] { "UpgradeSelection", "Ultimate", "UpgradeSelection" }));
                Assert.That((int)changes[2].before, Is.EqualTo(5));
                actor.SetUltimateInvulnerable(false); Assert.That(actor.IsInvulnerable, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(obj); }
        }
    }
}
