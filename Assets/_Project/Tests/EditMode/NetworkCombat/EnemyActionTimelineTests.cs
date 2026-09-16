using AstralShift.HellMaiden.AI;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class EnemyActionTimelineTests
    {
        private static EnemyActionState Single() => EnemyActionTimeline.Begin(42, 10,
            new[] { new EnemyStrikeTiming(.57f, .08f) }, .29f, .5f, Vector2.right, Vector2.zero);

        [Test]
        public void WarningDefinesSingleAttackAndHalfOpenWindowWithoutAnotherMessage()
        {
            var state = Single();
            Assert.That(EnemyActionTimeline.Resolve(state, state.WarningUntil - .00001).Phase, Is.EqualTo(EnemyAttackPresentationPhase.Warning));
            Assert.That(EnemyActionTimeline.Resolve(state, state.WarningUntil).Phase, Is.EqualTo(EnemyAttackPresentationPhase.Active));
            Assert.That(EnemyActionTimeline.Resolve(state, state.ActiveUntil).Phase, Is.EqualTo(EnemyAttackPresentationPhase.Recovery));
            Assert.That(EnemyActionTimeline.Resolve(state, state.RecoveryUntil).Phase, Is.EqualTo(EnemyAttackPresentationPhase.Inactive));
            Assert.That(state.ActiveUntil - state.WarningUntil, Is.EqualTo(.08).Within(.000001));
        }

        [Test]
        public void LowFrameRateSkipsTheWholeActiveInsteadOfMakingAnExtraHitFrame()
        {
            var state = Single();
            Assert.That(EnemyActionTimeline.Resolve(state, 10.9).Phase, Is.EqualTo(EnemyAttackPresentationPhase.Recovery));
            Assert.That(EnemyActionTimeline.Resolve(state, 12).Phase, Is.EqualTo(EnemyAttackPresentationPhase.Inactive));
            var active = EnemyActionTimeline.Resolve(state, state.WarningUntil);
            Assert.That(EnemyActionTimeline.Resolve(active, 9).Phase, Is.EqualTo(EnemyAttackPresentationPhase.Active));
        }

        [Test]
        public void PausedClockAndCancelledActionNeverAdvanceOrResurrect()
        {
            var state = Single();
            for (int i = 0; i < 100; i++) state = EnemyActionTimeline.Resolve(state, 10.2);
            Assert.That(state.Phase, Is.EqualTo(EnemyAttackPresentationPhase.Warning));
            state.Phase = EnemyAttackPresentationPhase.Cancelled;
            Assert.That(EnemyActionTimeline.Resolve(state, 10.6).Phase, Is.EqualTo(EnemyAttackPresentationPhase.Cancelled));
        }

        [Test]
        public void ValidFinalFrameContactSettlesAfterDeadlineButStaleContactsDoNot()
        {
            var state = Single(); double now = state.WarningUntil; bool alive = true;
            state = EnemyActionTimeline.Resolve(state, now);
            var window = new EnemyAttackWindow(() => now, () => alive);
            window.Bind(state, 3, 5);
            Assert.That(window.TryCapture(out var receipt), Is.True);
            now = state.ActiveUntil;
            Assert.That(window.TryCapture(out _), Is.False);
            Assert.That(window.CanSettle(receipt), Is.True, "Normal close settles an actual contact from the live window.");
            window.Bind(state, 4, 5);
            Assert.That(window.CanSettle(receipt), Is.False, "An assignment change invalidates pending contacts.");
            window.Bind(state, 3, 6);
            Assert.That(window.CanSettle(receipt), Is.False, "A reused attack instance has a new generation.");
            window.Bind(state, 3, 5); alive = false;
            Assert.That(window.CanSettle(receipt), Is.False);
            alive = true; window.Cancel();
            Assert.That(window.CanSettle(receipt), Is.False);
        }

        [Test]
        public void SingleAndComboUseTheSameDefinitionAndKeepOnlyOneFinalRecovery()
        {
            var state = EnemyActionTimeline.Begin(123, 1,
                new[] { new EnemyStrikeTiming(.3f, .2f), new EnemyStrikeTiming(.4f, .2f), new EnemyStrikeTiming(.2f, .2f) },
                .1f, .5f, Vector2.left, Vector2.zero);
            Assert.That(state.RecoveryUntil, Is.EqualTo(2.6).Within(.000001));
            var second = EnemyActionTimeline.Resolve(state, 1.6);
            Assert.That(second.StrikeIndex, Is.EqualTo(1));
            Assert.That(second.Phase, Is.EqualTo(EnemyAttackPresentationPhase.Warning));
            Assert.That(EnemyActionTimeline.HasPose(second), Is.False);
            second.PoseStrikeIndex = 1; second.LockedStrikeMask = 3;
            Assert.That(EnemyActionTimeline.HasPose(second), Is.True);
        }
    }
}
