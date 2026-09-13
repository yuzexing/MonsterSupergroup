using AstralShift.HellMaiden.AI;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class EnemyHandoffTests
    {
        [TestCase(EnemyAttackPresentationPhase.Active)]
        [TestCase(EnemyAttackPresentationPhase.Recovery)]
        public void ReceivingClockCannotRewindTheAcceptedActionPhase(EnemyAttackPresentationPhase phase)
        {
            var state = new EnemyActionState { Phase = phase, WarningUntil = 11, ActiveUntil = 12, RecoveryUntil = 13 };
            Assert.That(state.PhaseAt(10.99), Is.EqualTo(phase));
            Assert.That(state.PhaseAt(14), Is.EqualTo(EnemyAttackPresentationPhase.Inactive));
        }

        [Test]
        public void EndedPredictedImpulseKeepsItsReceiptAcrossOwnersWithoutExtendingExpiry()
        {
            var source = new EnemyPredictedKnockbackHistory();
            Assert.That(source.TryRemember(101, 10), Is.True);
            var next = new EnemyPredictedKnockbackHistory();
            next.Restore(source.Capture(11), 11);
            Assert.That(next.TryRemember(101, 11), Is.False);
            source.Restore(next.Capture(12), 12);
            Assert.That(source.Capture(13), Is.Empty, "An A→B→A handoff must not refresh receipt retention.");
            next.Acknowledge(101);
            Assert.That(next.Capture(12), Is.Empty);
            for (ulong i = 1; i <= EnemyPredictedKnockbackHistory.Capacity; i++) Assert.That(next.TryRemember(i, 20), Is.True);
            Assert.That(next.TryRemember(999, 20), Is.False);
        }

        [Test]
        public void NearestUsesEachEnemyPositionAndStableParticipantTiesAndExcludesInvalidAvatars()
        {
            var choice = new EnemyNearestTarget();
            choice.Consider(100, 9, Vector2.left, Vector2.zero, true);
            choice.Consider(200, 3, Vector2.right, Vector2.zero, true);
            choice.Consider(300, 1, Vector2.zero, Vector2.zero, false);
            Assert.That(choice.AvatarId, Is.EqualTo(200));
            choice = new EnemyNearestTarget();
            choice.Consider(200, 3, Vector2.right, Vector2.left * 5, true);
            choice.Consider(100, 9, Vector2.left, Vector2.left * 5, true);
            Assert.That(choice.AvatarId, Is.EqualTo(100));
            choice = new EnemyNearestTarget();
            choice.Consider(0, 0, Vector2.zero, Vector2.zero, true);
            choice.Consider(300, 1, new Vector2(float.NaN, 0), Vector2.zero, true);
            Assert.That(choice.AvatarId, Is.Zero);
        }

        [Test]
        public void LatestIntentCannotStarveCurrentEpochAndReturningToCurrentCancelsPending()
        {
            var progress = new EnemyHandoffProgress();
            progress.Begin(3, 10, true, 0);
            for (int i = 0; i < 10000; i++) progress.Request((uint)(20 + i % 2), 10, EnemyTargetChangeReason.Forced);
            Assert.That(progress.AwaitingEpoch, Is.EqualTo(3));
            Assert.That(progress.PendingTarget, Is.EqualTo(21));
            progress.Observe(2, .1);
            Assert.That(progress.AwaitingEpoch, Is.EqualTo(3));
            progress.Observe(3, .2);
            Assert.That(progress.AwaitingEpoch, Is.Zero);
            Assert.That(progress.Diagnostics.Completed, Is.EqualTo(1));
            Assert.That(progress.Diagnostics.Coalesced, Is.EqualTo(9999));
            progress.Request(10, 10, EnemyTargetChangeReason.Forced);
            Assert.That(progress.HasPending, Is.False);
        }

        [Test]
        public void FirstSnapshotTimeoutAndInvalidationDoNotWaitForOldEpoch()
        {
            var progress = new EnemyHandoffProgress();
            progress.Begin(3, 10, true, 20);
            Assert.That(progress.TimedOut(20.999), Is.False);
            Assert.That(progress.TimedOut(21), Is.True);
            progress.Begin(4, 20, true, 21);
            progress.Observe(3, 21.1);
            Assert.That(progress.AwaitingEpoch, Is.EqualTo(4));
            progress.Observe(4, 21.2);
            Assert.That(progress.TimedOut(30), Is.False);
        }

        [Test]
        public void RepeatedAssignmentIsIdempotentAndABARejectsOldPackets()
        {
            var registry = new ServerEnemySimulationRegistry();
            registry.RegisterEnemy(100, new Vector2(42, 3), 0);
            var a = registry.AssignClientOwner(100, 10, 10);
            Assert.That(registry.AssignClientOwner(100, 10, 10).Epoch, Is.EqualTo(a.Epoch));
            registry.AssignClientOwner(100, 20, 20);
            var returned = registry.AssignClientOwner(100, 10, 10);
            var snapshot = new EnemySimulationSnapshot { EnemyEntityId = 100, AssignmentEpoch = a.Epoch, Sequence = 1, SampleNetworkTime = 1 };
            Assert.That(registry.TryAcceptClientSnapshot(10, snapshot), Is.EqualTo(EnemySnapshotRejectionReason.WrongEpoch));
            snapshot.AssignmentEpoch = returned.Epoch;
            Assert.That(registry.TryAcceptClientSnapshot(20, snapshot), Is.EqualTo(EnemySnapshotRejectionReason.WrongOwner));
            Assert.That(registry.TryAcceptClientSnapshot(10, snapshot), Is.EqualTo(EnemySnapshotRejectionReason.None));
        }

        [TestCase(10.5, EnemyAttackPresentationPhase.Warning)]
        [TestCase(11.2, EnemyAttackPresentationPhase.Active)]
        [TestCase(12.5, EnemyAttackPresentationPhase.Recovery)]
        [TestCase(14, EnemyAttackPresentationPhase.Inactive)]
        public void ActionTimelineSeeksWithoutRestartingPhases(double now, EnemyAttackPresentationPhase expected)
        {
            var action = new EnemyActionState { ActionId = 25, Phase = EnemyAttackPresentationPhase.Warning,
                WarningStartedAt = 10, WarningUntil = 11, ActiveUntil = 12, RecoveryUntil = 13, NextAttackAt = 14 };
            Assert.That(action.PhaseAt(now), Is.EqualTo(expected));
            Assert.That(action.ActionId, Is.EqualTo(25));
            Assert.That(action.NextAttackAt, Is.EqualTo(14));
        }

        [Test]
        public void ReliableCheckpointCannotRegressLatestPoseAndMalformedRuntimeIsRejected()
        {
            var registry = new ServerEnemySimulationRegistry();
            registry.RegisterEnemy(100, Vector2.zero, 0);
            var assignment = registry.AssignClientOwner(100, 10, 10);
            var pose = new EnemySimulationSnapshot { EnemyEntityId = 100, AssignmentEpoch = assignment.Epoch, Sequence = 1, SampleNetworkTime = 3,
                Position = Vector2.right * 8, Runtime = new EnemySimulationRuntimeState { Action = new EnemyActionState { ActionId = 15 } } };
            registry.TryAcceptClientSnapshot(10, pose);
            pose.SampleNetworkTime = 2; pose.Position = Vector2.zero;
            registry.RecordCheckpoint(new EnemySimulationCheckpoint { Movement = pose });
            registry.TryGetLatestSnapshot(100, out var latest);
            Assert.That(latest.Position.x, Is.EqualTo(8));
            Assert.That(latest.Runtime.Action.ActionId, Is.EqualTo(15));
            pose.Sequence = 2; pose.Runtime.Action.ActiveUntil = double.NaN;
            Assert.That(registry.TryAcceptClientSnapshot(10, pose), Is.EqualTo(EnemySnapshotRejectionReason.InvalidValue));
        }
    }
}
