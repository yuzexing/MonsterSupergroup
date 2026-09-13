using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using MonsterSupergroup.GAS;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class SnapshotAndDotReconciliationTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void CheckpointAndMovementHaveIndependentWatermarks(bool client, bool checkpointFirst)
        {
            var registry = new ServerEnemySimulationRegistry();
            registry.RegisterEnemy(100, Vector2.zero, 0);
            var assignment = client ? registry.AssignClientOwner(100, 1, 1) : registry.AssignServerAuthoritative(100, 1);
            void Accept(EnemySimulationSnapshot value)
            {
                if (client) Assert.That(registry.TryAcceptClientSnapshot(1, value), Is.EqualTo(EnemySnapshotRejectionReason.None));
                else Assert.DoesNotThrow(() => registry.RecordServerSnapshot(value));
            }
            EnemySimulationSnapshot Pose(uint sequence, double time, float x) => new EnemySimulationSnapshot
            { EnemyEntityId = 100, AssignmentEpoch = assignment.Epoch, Sequence = sequence, SampleNetworkTime = time, Position = new Vector2(x, 0) };
            Accept(Pose(1, 1, 1));
            var checkpoint = new EnemySimulationCheckpoint { Movement = Pose(0, 2, 9) };
            if (checkpointFirst) registry.RecordCheckpoint(checkpoint);
            Accept(Pose(2, 2, 2));
            if (!checkpointFirst) registry.RecordCheckpoint(checkpoint);
            registry.TryGetLatestSnapshot(100, out var latest);
            Assert.That(latest.Position.x, Is.EqualTo(9), "Equal-time reliable action state wins the handoff seed.");
            if (client) Assert.That(registry.TryAcceptClientSnapshot(1, Pose(3, 2, 3)), Is.EqualTo(EnemySnapshotRejectionReason.StaleTimestamp));
            else Assert.Throws<ArgumentException>(() => registry.RecordServerSnapshot(Pose(3, 2, 3)));
            Accept(Pose(3, 3, 3));
            registry.TryGetLatestSnapshot(100, out latest);
            Assert.That(latest.Position.x, Is.EqualTo(3));
            assignment = registry.AssignServerFallback(100, 2);
            Assert.DoesNotThrow(() => registry.RecordServerSnapshot(Pose(1, 3, 3)), "A new epoch resets the movement watermark.");
        }

        [TestCase(EnemyStatusID.Burn, 0)]
        [TestCase(EnemyStatusID.Burn, 1)]
        [TestCase(EnemyStatusID.Burn, 2)]
        [TestCase(EnemyStatusID.Poison, 0)]
        [TestCase(EnemyStatusID.Poison, 1)]
        [TestCase(EnemyStatusID.Poison, 2)]
        [TestCase(EnemyStatusID.Bleed, 0)]
        [TestCase(EnemyStatusID.Bleed, 1)]
        [TestCase(EnemyStatusID.Bleed, 2)]
        public void LateDuplicateOrPostExpiryConfirmationNeverRepeatsTicks(EnemyStatusID status, int delivery)
        {
            var ticks = new List<StatusTick>();
            var runtime = new StatusController(ticks.Add);
            runtime.Apply(Application(status));
            var baseline = runtime.GetInstances(status)[0];
            if (delivery == 1) runtime.UpsertCanonical(baseline);
            runtime.Advance(delivery == 2 ? .91f : .35f);
            runtime.UpsertCanonical(baseline);
            runtime.UpsertCanonical(baseline);
            if (delivery != 2)
            {
                runtime.Advance(.25f);
                Assert.That(ticks.Count, Is.EqualTo(2), "Confirmation preserves the partial tick clock.");
            }
            runtime.Advance(1f);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, ticks.Select(t => t.TickIndex));
            Assert.That(runtime.Has(status), Is.False);
        }

        [Test]
        public void NewPredictionRoundSurvivesOldAcknowledgementAndGetsItsOwnTicks()
        {
            var ticks = new List<StatusTick>();
            var runtime = new StatusController(ticks.Add);
            runtime.Apply(Application(EnemyStatusID.Burn));
            var first = runtime.GetInstances(EnemyStatusID.Burn)[0];
            runtime.Advance(.35f);
            runtime.Apply(Application(EnemyStatusID.Burn));
            var second = runtime.GetInstances(EnemyStatusID.Burn)[0];
            Assert.That(second.ApplicationRevision, Is.EqualTo(2));
            Assert.That(runtime.UpsertCanonical(first), Is.False);
            Assert.That(runtime.UpsertCanonical(Canonical(second, 2, 0)), Is.True);
            runtime.Advance(.91f);
            CollectionAssert.AreEqual(new[] { 1u, 2u, 2u, 2u }, ticks.Select(t => t.Instance.ApplicationRevision));
            CollectionAssert.AreEqual(new[] { 1, 1, 2, 3 }, ticks.Select(t => t.TickIndex));
            runtime.Clear();
            runtime.Apply(Application(EnemyStatusID.Burn));
            Assert.That(runtime.GetInstances(EnemyStatusID.Burn)[0].ApplicationRevision, Is.EqualTo(1));
        }

        [Test]
        public void CanonicalProgressCanAdvanceWithinOneVersionWithoutDispatchingPastTicks()
        {
            var ticks = new List<StatusTick>();
            var runtime = new StatusController(ticks.Add);
            var first = Canonical(ApplicationInstance(), 1, 0);
            runtime.UpsertCanonical(first);
            runtime.Advance(.1f);
            runtime.UpsertCanonical(first.WithProgress(2));
            runtime.UpsertCanonical(first);
            runtime.Advance(.21f);
            Assert.That(ticks, Is.Empty);
            runtime.Advance(.1f);
            CollectionAssert.AreEqual(new[] { 3 }, ticks.Select(t => t.TickIndex));
        }

        [Test]
        public void ReplicaKeepsRemovalAndProgressWhenOldSnapshotsArriveOrControllerRebinds()
        {
            var replica = new CanonicalWorldReplica();
            var state = CanonicalStatusState.From(ApplicationInstance());
            state.CompletedTicks = 2;
            void Apply(CanonicalStatusState value) => replica.Apply(new CanonicalWorldBatch { Statuses = new[] { value } });
            Apply(state);
            var old = state; old.CompletedTicks = 0;
            Apply(old);
            Assert.That(replica.ReadStatuses(100)[0].CompletedTicks, Is.EqualTo(2));
            Apply(CanonicalStatusState.Removal(state.ToStatusInstance(), 2));
            Apply(state);
            var ticks = new List<StatusTick>();
            var runtime = new StatusController(ticks.Add);
            replica.RegisterStatusController(100, runtime);
            runtime.Advance(5f);
            Assert.That(ticks, Is.Empty);
            Assert.That(replica.ReadStatuses(100), Is.Empty);
            replica.ForgetEntity(100);
            Apply(state);
            Assert.That(replica.ReadStatuses(100), Has.Count.EqualTo(1));
        }

        [Test]
        public void StateAndDamageWireRoundTripPreservesApplicationIdentity()
        {
            var state = CanonicalStatusState.From(Canonical(ApplicationInstance(), 4, 1, 3));
            var writer = new NetworkWriter(); writer.Write(state);
            var read = new NetworkReader(writer.ToArray()).Read<CanonicalStatusState>();
            Assert.That(read.ToStatusInstance().ApplicationRevision, Is.EqualTo(3));
            var result = new CombatResult { EventId = 5, StatusInstanceId = state.InstanceId, StatusApplicationRevision = 3 };
            writer = new NetworkWriter(); writer.Write(result);
            var damage = new NetworkReader(writer.ToArray()).Read<CombatResult>();
            Assert.That(damage.StatusInstanceId, Is.EqualTo(state.InstanceId));
            Assert.That(damage.StatusApplicationRevision, Is.EqualTo(3));
            var mutation = new StatusMutation { InstanceId = state.InstanceId, ApplicationRevision = 3 };
            writer = new NetworkWriter(); writer.Write(mutation);
            Assert.That(new NetworkReader(writer.ToArray()).Read<StatusMutation>().ApplicationRevision, Is.EqualTo(3));
        }

        [Test]
        public void OldRemovalCannotEraseNewPredictionOrNewRoundWithRestartedVersion()
        {
            var ticks = new List<StatusTick>();
            var runtime = new StatusController(ticks.Add);
            runtime.Apply(Application(EnemyStatusID.Burn));
            var first = runtime.GetInstances(EnemyStatusID.Burn)[0];
            runtime.UpsertCanonical(Canonical(first, 8, 0));
            runtime.Advance(1f);
            runtime.Apply(Application(EnemyStatusID.Burn));
            var second = runtime.GetInstances(EnemyStatusID.Burn)[0];
            Assert.That(second.ApplicationRevision, Is.EqualTo(2));
            Assert.That(runtime.RemoveCanonical(first.InstanceId, 9, first.ApplicationRevision), Is.False);
            Assert.That(runtime.UpsertCanonical(Canonical(second, 1, 0)), Is.True);
            Assert.That(runtime.UpsertCanonical(Canonical(first, 10, 0)), Is.False);
            runtime.Advance(1f);
            Assert.That(ticks.Count, Is.EqualTo(6));
            Assert.That(ticks.Select(t => $"{t.Instance.ApplicationRevision}:{t.TickIndex}").Distinct().Count(), Is.EqualTo(6));
        }

        [Test]
        public void TwoSourcesKeepIndependentProgressWhenOneConfirmationIsLate()
        {
            var ticks = new List<StatusTick>();
            var runtime = new StatusController(ticks.Add);
            runtime.Apply(Application(EnemyStatusID.Poison));
            var first = runtime.GetInstances(EnemyStatusID.Poison)[0];
            runtime.Apply(new StatusApplication(first.Definition, 3, 3, .3f, 10,
                instanceId: new StatusInstanceId(202), sourcePlayerId: 2, sourceEntityId: 2, targetEntityId: 100));
            var second = runtime.GetInstances(EnemyStatusID.Poison)[1];
            runtime.Advance(.35f);
            runtime.UpsertCanonical(Canonical(second, 1, 0));
            runtime.UpsertCanonical(Canonical(first, 1, 0));
            runtime.Advance(.6f);
            Assert.That(ticks.Count, Is.EqualTo(6));
            Assert.That(ticks.Select(t => $"{t.Instance.InstanceId}:{t.Instance.ApplicationRevision}:{t.TickIndex}").Distinct().Count(), Is.EqualTo(6));
        }

        [Test]
        public void CompletionConfirmationRemovesBindingsWithoutReplayingGameplay()
        {
            var runtime = new StatusController(_ => Assert.Fail("Completed progress must not dispatch past ticks."));
            var instance = ApplicationInstance();
            runtime.UpsertCanonical(instance);
            int removed = 0;
            runtime.Changed += change => { if (change.Kind == StatusChangeKind.Removed) removed++; };
            runtime.UpsertCanonical(instance.WithProgress(3));
            runtime.UpsertCanonical(instance.WithProgress(3));
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(runtime.Has(EnemyStatusID.Burn), Is.False);
        }

        private static StatusApplication Application(EnemyStatusID status) => new StatusApplication(
            new StatusDefinition(status, StatusStackMode.Add, 20), 3, 3, .3f, 10,
            instanceId: new StatusInstanceId(101), sourcePlayerId: 1, sourceEntityId: 1, targetEntityId: 100);

        private static StatusInstance ApplicationInstance()
        {
            var runtime = new StatusController(_ => { }); runtime.Apply(Application(EnemyStatusID.Burn));
            return runtime.GetInstances(EnemyStatusID.Burn)[0];
        }

        private static StatusInstance Canonical(StatusInstance source, uint version, int completed, uint revision = 0) =>
            new StatusInstance(source.InstanceId, source.Definition, source.SourcePlayerId, source.SourceEntityId,
                source.TargetEntityId, source.Stack, source.StartTime, source.Duration, source.ExecutionAuthority,
                version, source.TickDamage, source.TotalTicks, completed, source.TickInterval, source.Priority,
                source.DamageSourceId, source.SourceContext, source.Magnitude, revision == 0 ? source.ApplicationRevision : revision);
    }
}
