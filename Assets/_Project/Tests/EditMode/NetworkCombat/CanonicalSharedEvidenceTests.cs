using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CanonicalSharedEvidenceTests
    {
        private static readonly MethodInfo Receive = typeof(NetworkCombatWorld).GetMethod("ApplyCanonicalForRound", BindingFlags.Instance | BindingFlags.NonPublic);
        private IDiagnosticSink previous;
        private Scene preview;
        private GameObject owner;
        private NetworkCombatWorld world;
        private RecordingSink sink;

        [SetUp] public void SetUp()
        {
            previous = CombatEvidence.Sink;
            preview = EditorSceneManager.NewPreviewScene();
            owner = new GameObject("CanonicalSharedEvidenceTest") { hideFlags = HideFlags.HideAndDontSave };
            owner.SetActive(false); SceneManager.MoveGameObjectToScene(owner, preview);
            world = owner.AddComponent<NetworkCombatWorld>();
        }
        [TearDown] public void TearDown()
        {
            using (CombatEvidence.Suppress()) if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
            sink?.Dispose(); CombatEvidence.Sink = previous;
            if (preview.IsValid() && preview.isLoaded) EditorSceneManager.ClosePreviewScene(preview);
        }
        private static CanonicalWorldBatch Batch() => new CanonicalWorldBatch { ServerSequence = 9,
            Entities = new[] { new CanonicalEntityState { EntityId = 77, Kind = (byte)CombatEntityKind.Player,
                Health = 10, MaxHealth = 10, Alive = true, StateVersion = 1 } } };
        private void Apply(CanonicalWorldBatch batch, uint? round = null) => Receive.Invoke(world, new object[] { batch, round ?? NetworkCombatWorld.CurrentRound });
        private DiagnosticRecord Received => sink.Records.Single(record => record.stage == "network.canonical");
        private object ReplicaInput => ((object[])sink.Records.Single(record => record.stage == "replay.input" && record.operation == "Apply").input)[0];

        [Test] public void ReceivedAndReplicaInputShareOneFrozenVersionUntilBothQueueLeasesEnd()
        {
            var sharedSink = new SharedSink(); sink = sharedSink; CombatEvidence.Sink = sink;
            var batch = Batch();
            // This callback runs after Replica.Apply and may amend the caller's mutable transport arrays.
            world.CanonicalBatchReceived += value => value.Entities[0].Health = 99;
            Apply(batch);
            var handle = ((CanonicalReceiveEvidence)Received.input).batch as SharedEvidencePayload;
            Assert.That(handle, Is.Not.Null); Assert.That(ReplicaInput, Is.SameAs(handle));
            Assert.That(batch.Entities[0].Health, Is.EqualTo(99));
            Assert.That(((CanonicalWorldBatch)handle.CopyValueForTests()).Entities[0].Health, Is.EqualTo(10));
            Assert.That(world.Replica.TryGetEntity(77, out var actual), Is.True); Assert.That(actual.Health, Is.EqualTo(10));
            Assert.That(sharedSink.Memory.Used, Is.EqualTo(handle.RetainedBytes));
            sink.Dispose(); Assert.That(sharedSink.Memory.Used, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => handle.AcquireLease());
        }

        [Test] public void BatchesWithPresentationEdgesKeepDistinctVersionsAcrossThePreApplyBoundary()
        {
            var sharedSink = new SharedSink(); sink = sharedSink; CombatEvidence.Sink = sink;
            var batch = Batch(); batch.EnemyHitPresentations = new[] { new EnemyHitPresentation { Damage = 1 } };
            // Simulate an observer mutating the arrays after receipt but before Apply; no pooled visual/Steam runtime is needed.
            sink.AfterReceive = () => { batch.Entities[0].Health = 6; batch.EnemyHitPresentations[0].HasPosition = true; };
            Apply(batch);
            var receipt = (CanonicalWorldBatch)((CanonicalReceiveEvidence)Received.input).batch;
            var applied = (CanonicalWorldBatch)ReplicaInput;
            Assert.That(receipt.Entities[0].Health, Is.EqualTo(10)); Assert.That(receipt.EnemyHitPresentations[0].HasPosition, Is.False);
            Assert.That(applied.Entities[0].Health, Is.EqualTo(6)); Assert.That(applied.EnemyHitPresentations[0].HasPosition, Is.True);
            Assert.That(world.Replica.TryGetEntity(77, out var actual), Is.True); Assert.That(actual.Health, Is.EqualTo(6));
            Assert.That(sharedSink.Memory.Used, Is.Zero);
        }

        [Test] public void WrongRoundDoesNotApplyOrRetainASharedVersion()
        {
            var sharedSink = new SharedSink(); sink = sharedSink; CombatEvidence.Sink = sink;
            Apply(Batch(), unchecked(NetworkCombatWorld.CurrentRound + 1));
            Assert.That(Received.outcome, Is.EqualTo("Ignored")); Assert.That(Received.reason, Is.EqualTo("WrongRound"));
            Assert.That(((CanonicalReceiveEvidence)Received.input).batch, Is.TypeOf<CanonicalWorldBatch>());
            Assert.That(sink.Records.Any(record => record.stage == "replay.input"), Is.False);
            Assert.That(world.Replica.TryGetEntity(77, out _), Is.False); Assert.That(sharedSink.Memory.Used, Is.Zero);
        }

        [TestCase(false)] [TestCase(true)]
        public void LegacyOrInsufficientSharedBudgetFallsBackToDetachedInputsWithoutChangingGameplay(bool limitedBudget)
        {
            sink = limitedBudget ? new SharedSink(512) : new RecordingSink(); CombatEvidence.Sink = sink;
            var batch = Batch(); Apply(batch); batch.Entities[0].Health = 99;
            Assert.That(((CanonicalWorldBatch)((CanonicalReceiveEvidence)Received.input).batch).Entities[0].Health, Is.EqualTo(10));
            Assert.That(((CanonicalWorldBatch)ReplicaInput).Entities[0].Health, Is.EqualTo(10));
            Assert.That(world.Replica.TryGetEntity(77, out var actual), Is.True); Assert.That(actual.Health, Is.EqualTo(10));
            if (sink is SharedSink sharedSink) Assert.That(sharedSink.Memory.Used, Is.Zero);
        }

        private class RecordingSink : IDiagnosticSink, IDisposable
        {
            public readonly List<DiagnosticRecord> Records = new();
            private readonly List<IDisposable> leases = new();
            public Action AfterReceive;
            public bool TryWrite(DiagnosticRecord record)
            {
                var copy = record.Copy();
                copy.input = DiagnosticPayload.Freeze(copy.input); copy.before = DiagnosticPayload.Freeze(copy.before); copy.after = DiagnosticPayload.Freeze(copy.after);
                SharedEvidencePayload.CollectLeases(copy.input, leases); SharedEvidencePayload.CollectLeases(copy.before, leases); SharedEvidencePayload.CollectLeases(copy.after, leases);
                Records.Add(copy); if (copy.stage == "network.canonical") AfterReceive?.Invoke(); return true;
            }
            public string RegisterEngine(object engine, string domain, Func<object, object> capture) => domain + ".shared-test";
            public void Dispose() { foreach (var lease in leases) lease.Dispose(); leases.Clear(); }
        }
        private sealed class SharedSink : RecordingSink, IDiagnosticSharedPayloadSink
        {
            public DiagnosticMemoryBudget Memory { get; }
            public SharedSink(long bytes = 64 << 20) { Memory = new DiagnosticMemoryBudget(bytes); }
        }
    }
}
