using System;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class PlayerUltimateRuntimeTests
    {
        [Test]
        public void ChargeIsGrantedOnceAndConsumedOnce()
        {
            var runtime = new PlayerUltimateRuntime();
            Assert.That(runtime.TryConsume(10, 4.62, 3), Is.False);
            Assert.That(runtime.TryGrantCharge(), Is.True);
            Assert.That(runtime.TryGrantCharge(), Is.False);
            Assert.That(runtime.TryConsume(10, 4.62, 3), Is.True);
            Assert.That(runtime.HasCharge, Is.False);
            Assert.That(runtime.TryConsume(20, 4.62, 3), Is.False);
        }

        [Test]
        public void NewChargeCanBeHeldDuringAnAttackButCannotOverlapItsWaves()
        {
            var runtime = new PlayerUltimateRuntime();
            runtime.TryGrantCharge(); runtime.TryConsume(10, 4.62, 3);
            Assert.That(runtime.TryGrantCharge(), Is.True);
            Assert.That(runtime.TryConsume(14.61, 4.62, 3), Is.False);
            Assert.That(runtime.TryConsume(runtime.Capture().ActiveUntil, 4.62, 3), Is.True);
        }

        [Test]
        public void ReconnectKeepsSpentChargeAndAbsoluteDeadlinesWithoutReplayingAnAttack()
        {
            var original = new PlayerUltimateRuntime();
            original.TryGrantCharge(); original.TryConsume(10, 4.62, 3);
            var restored = new PlayerUltimateRuntime(); restored.Restore(original.Capture());
            Assert.That(restored.IsInvulnerable(12), Is.True);
            Assert.That(restored.IsInvulnerable(13), Is.False);
            Assert.That(restored.CanUse(20), Is.False);
            restored.TryGrantCharge();
            Assert.That(restored.CanUse(20), Is.True);
            Assert.That(original.HasCharge, Is.False);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(-1d)]
        public void InvalidTimesDoNotConsumeCharge(double time)
        {
            var runtime = new PlayerUltimateRuntime(); runtime.TryGrantCharge();
            Assert.Throws<ArgumentOutOfRangeException>(() => runtime.TryConsume(time, 4.62, 3));
            Assert.That(runtime.HasCharge, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Restore(new PlayerUltimateSnapshot { ActiveUntil = time }));
            Assert.That(runtime.HasCharge, Is.True);
        }

        [Test]
        public void InvalidProtectionDurationDoesNotMutateState()
        {
            var runtime = new PlayerUltimateRuntime(); runtime.TryGrantCharge();
            Assert.Throws<ArgumentOutOfRangeException>(() => runtime.TryConsume(10, 3, 4));
            Assert.Throws<ArgumentException>(() => runtime.Restore(new PlayerUltimateSnapshot { ActiveUntil = 1, InvulnerableUntil = 2 }));
            Assert.That(runtime.HasCharge, Is.True);
        }
    }
}
