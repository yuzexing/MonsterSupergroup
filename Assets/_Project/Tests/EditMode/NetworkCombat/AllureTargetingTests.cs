using AstralShift.HellMaiden.AI;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class AllureTargetingTests
    {
        private static ServerEnemySimulationRegistry Setup(out EnemySimulationAssignment assignment)
        {
            var registry = new ServerEnemySimulationRegistry();
            registry.RegisterEnemy(100, Vector2.zero, 0);
            assignment = registry.AssignClientOwner(100, 10, 10);
            return registry;
        }

        [Test]
        public void RedirectDoesNotChangeSimulatorEpochOrResetAcceptedMovementSequence()
        {
            var registry = Setup(out var before);
            var pose = new EnemySimulationSnapshot
            {
                EnemyEntityId = 100, AssignmentEpoch = before.Epoch, Sequence = 12, SampleNetworkTime = 1,
                Position = Vector2.right, Runtime = new EnemySimulationRuntimeState
                { Action = new EnemyActionState { ActionId = 88, Phase = EnemyAttackPresentationPhase.Active } }
            };
            Assert.That(registry.TryAcceptClientSnapshot(10, pose), Is.EqualTo(EnemySnapshotRejectionReason.None));
            var intent = registry.SetAggroTarget(100, 20, 10);
            registry.TryGetAssignment(100, out var after);
            Assert.That(after.Epoch, Is.EqualTo(before.Epoch));
            Assert.That(after.SimulationOwnerPlayerId, Is.EqualTo(10));
            Assert.That(after.AggroTargetPlayerId, Is.EqualTo(20));
            Assert.That(intent.NeedsHandoff(after), Is.True);
            registry.TryGetLatestSnapshot(100, out var saved);
            Assert.That(saved.Runtime.Action.ActionId, Is.EqualTo(88));
            Assert.That(registry.TryAcceptClientSnapshot(10, pose), Is.EqualTo(EnemySnapshotRejectionReason.StaleSequence));
            pose.Sequence++; pose.SampleNetworkTime += .1;
            Assert.That(registry.TryAcceptClientSnapshot(20, pose), Is.EqualTo(EnemySnapshotRejectionReason.WrongOwner));
            Assert.That(registry.TryAcceptClientSnapshot(10, pose), Is.EqualTo(EnemySnapshotRejectionReason.None));
        }

        [Test]
        public void RealHandoffAdvancesEpochAndPreservesTargetIntent()
        {
            var registry = Setup(out var before);
            var target = registry.SetAggroTarget(100, 20, 10);
            var transferred = registry.AssignClientOwner(100, 20, 20);
            registry.TryGetTargetState(100, out var finalTarget);
            Assert.That(transferred.Epoch, Is.EqualTo(before.Epoch + 1));
            Assert.That(finalTarget.Revision, Is.EqualTo(target.Revision));
            Assert.That(finalTarget.NeedsHandoff(transferred), Is.False);
        }

        [Test]
        public void OldDecoyExpiryCannotOverwriteLaterRedirectOrNewDecoy()
        {
            var registry = Setup(out var before);
            var first = registry.SetDecoyTarget(100, 10, 81, Vector2.right, 5);
            var redirect = registry.SetAggroTarget(100, 20, 10);
            Assert.That(redirect.Revision, Is.GreaterThan(first.Revision));
            Assert.That(redirect.HasDecoy, Is.False);
            Assert.That(registry.ClearDecoyTarget(100, 10, 81, out _), Is.False);
            var second = registry.SetDecoyTarget(100, 20, 82, Vector2.left, 7);
            Assert.That(registry.ClearDecoyTarget(100, 10, 81, out _), Is.False);
            Assert.That(registry.ClearDecoyTarget(100, 20, 81, out _), Is.False);
            Assert.That(registry.ClearDecoyTarget(100, 20, 82, out var cleared), Is.True);
            Assert.That(cleared.AggroPlayerId, Is.EqualTo(20));
            Assert.That(cleared.Revision, Is.EqualTo(second.Revision + 1));
            registry.TryGetAssignment(100, out var after);
            Assert.That(after.Epoch, Is.EqualTo(before.Epoch));
            Assert.That(cleared.NeedsHandoff(after), Is.True, "Decoy expiry resumes the prior pending transfer.");
        }

        [Test]
        public void ReturningToOriginalSimulatorCancelsTransferWithoutANewEpoch()
        {
            var registry = Setup(out var initial);
            registry.SetAggroTarget(100, 20, 10);
            var target = registry.SetAggroTarget(100, 10, 20);
            registry.TryGetAssignment(100, out var final);
            Assert.That(target.NeedsHandoff(final), Is.False);
            Assert.That(final.Epoch, Is.EqualTo(initial.Epoch));
        }

        [Test]
        public void EmergencyServerFallbackPreservesDecoyAndPendingRecipient()
        {
            var registry = Setup(out var initial);
            registry.SetAggroTarget(100, 20, 10);
            var decoy = registry.SetDecoyTarget(100, 20, 81, Vector2.right, 5);
            var fallback = registry.AssignServerFallback(100, 20);
            registry.TryGetTargetState(100, out var target);
            Assert.That(fallback.Epoch, Is.EqualTo(initial.Epoch + 1));
            Assert.That(target.Revision, Is.EqualTo(decoy.Revision));
            Assert.That(target.MatchesDecoy(20, 81), Is.True);
            Assert.That(target.NeedsHandoff(fallback), Is.True);
        }

        [Test]
        public void ClearingQueuedHandoffDoesNotPretendNewSimulatorReportedSnapshot()
        {
            var progress = new EnemyHandoffProgress();
            progress.Begin(3, 20, true, 1);
            progress.Request(10, 20, EnemyTargetChangeReason.Forced);
            progress.CancelPending();
            Assert.That(progress.HasPending, Is.False);
            Assert.That(progress.AwaitingEpoch, Is.EqualTo(3));
            Assert.That(progress.Diagnostics.AwaitingFirstSnapshot, Is.True);
            progress.Observe(2, 1.1);
            Assert.That(progress.AwaitingEpoch, Is.EqualTo(3));
            progress.Observe(3, 1.2);
            Assert.That(progress.Diagnostics.Completed, Is.EqualTo(1));
        }
    }
}
