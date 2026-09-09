using System;
using System.Linq;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class PlayerDashRuntimeTests
    {
        [Test]
        public void ChargesRechargeIndependentlyAndARepeatedUseCannotBypassTheChainGate()
        {
            var runtime = new PlayerDashRuntime(3);
            Assert.That(runtime.TryConsume(0d, 0.2d, 5d), Is.True);
            Assert.That(runtime.NextUseAt, Is.EqualTo(0.35d).Within(0.0000001d));
            Assert.That(runtime.TryConsume(0d, 0.2d, 5d), Is.False);
            Assert.That(runtime.TryConsume(0.34d, 0.2d, 5d), Is.False);
            Assert.That(runtime.TryConsume(runtime.NextUseAt, 0.2d, 1d), Is.True);
            Assert.That(runtime.AvailableCharges, Is.EqualTo(1));
            Assert.That(runtime.Refresh(1.34d), Is.False);
            Assert.That(runtime.Refresh(1.35d), Is.True, "The later use has its own earlier recharge deadline.");
            Assert.That(runtime.AvailableCharges, Is.EqualTo(2));
            Assert.That(runtime.Refresh(5d), Is.True);
            Assert.That(runtime.AvailableCharges, Is.EqualTo(3));
            Assert.That(runtime.Refresh(5d), Is.False);
        }

        [Test]
        public void ExhaustionDoesNotMoveTheGateOrAddAnExtraRecharge()
        {
            var runtime = new PlayerDashRuntime(1);
            Assert.That(runtime.TryConsume(2d, 0.3d, 8d), Is.True);
            double gate = runtime.NextUseAt;
            Assert.That(runtime.TryConsume(3d, 0.3d, 8d), Is.False);
            Assert.That(runtime.NextUseAt, Is.EqualTo(gate));
            Assert.That(runtime.Capture(3d).RechargeReadyAt, Is.EqualTo(new[] { 10d }));
            Assert.That(runtime.TryConsume(10d, 0.3d, 8d), Is.True, "A consume attempt refreshes explicitly supplied time first.");
        }

        [Test]
        public void ALongDashRetainsItsChainGateEvenWhenItsChargeHasAlreadyReturned()
        {
            var runtime = new PlayerDashRuntime(1);
            Assert.That(runtime.TryConsume(0d, 2d, 0.2d), Is.True);
            runtime.Refresh(1d);
            Assert.That(runtime.AvailableCharges, Is.EqualTo(1));
            Assert.That(runtime.TryConsume(2d, 0d, 1d), Is.False);
            Assert.That(runtime.TryConsume(2.15d, 0d, 1d), Is.True);
        }

        [Test]
        public void CapacityDowngradeKeepsAllPendingRechargesWithoutGiftingACharge()
        {
            var runtime = new PlayerDashRuntime(3);
            Assert.That(runtime.TryConsume(0d, 0d, 1d), Is.True);
            Assert.That(runtime.TryConsume(0.2d, 0d, 2d), Is.True);
            Assert.That(runtime.TryConsume(0.4d, 0d, 3d), Is.True);
            runtime.ConfigureCapacity(1);
            Assert.That(runtime.AvailableCharges, Is.Zero);
            Assert.That(runtime.Capture(0.4d).RechargeReadyAt.Length, Is.EqualTo(3));
            runtime.Refresh(1d);
            Assert.That(runtime.AvailableCharges, Is.Zero);
            runtime.Refresh(2.2d);
            Assert.That(runtime.AvailableCharges, Is.Zero);
            runtime.Refresh(3.4d);
            Assert.That(runtime.AvailableCharges, Is.EqualTo(1));
        }

        [Test]
        public void ChangingCapacityPreservesSpentChargesAndCannotResetThemByTogglingZero()
        {
            var runtime = new PlayerDashRuntime(2);
            runtime.TryConsume(0d, 0d, 10d);
            runtime.ConfigureCapacity(2);
            Assert.That(runtime.AvailableCharges, Is.EqualTo(1));
            runtime.ConfigureCapacity(0);
            Assert.That(runtime.AvailableCharges, Is.Zero);
            Assert.That(runtime.TryConsume(1d, 0d, 10d), Is.False);
            runtime.ConfigureCapacity(3);
            Assert.That(runtime.AvailableCharges, Is.EqualTo(2));
            Assert.That(runtime.Capture(1d).RechargeReadyAt, Is.EqualTo(new[] { 10d }));
        }

        [Test]
        public void RestoreAdvancesAbsoluteOfflineDeadlinesAndPreservesTheRemainingGate()
        {
            var source = new PlayerDashRuntime(2);
            source.TryConsume(20d, 0.2d, 2d);
            source.TryConsume(21d, 4d, 8d);
            PlayerDashSnapshot snapshot = source.Capture(21d);
            var restored = new PlayerDashRuntime();
            restored.Restore(snapshot, 24d);
            Assert.That(restored.MaxCharges, Is.EqualTo(2));
            Assert.That(restored.AvailableCharges, Is.EqualTo(1));
            Assert.That(restored.NextUseAt, Is.EqualTo(25.15d));
            Assert.That(restored.TryConsume(25d, 0d, 1d), Is.False);
            Assert.That(restored.Capture(24d).RechargeReadyAt, Is.EqualTo(new[] { 29d }));
            restored.Restore(snapshot, 30d);
            Assert.That(restored.AvailableCharges, Is.EqualTo(2));
            Assert.That(restored.TryConsume(30d, 0d, 2d), Is.True);
        }

        [Test]
        public void CaptureAndRestoreNeverShareMutableArraysWithEitherRuntime()
        {
            var source = new PlayerDashRuntime(2);
            source.TryConsume(0d, 0d, 10d);
            PlayerDashSnapshot transport = source.Capture(0d);
            var restored = new PlayerDashRuntime();
            restored.Restore(transport, 0d);
            transport.RechargeReadyAt[0] = 0d;
            Assert.That(source.Capture(0d).RechargeReadyAt, Is.EqualTo(new[] { 10d }));
            Assert.That(restored.Capture(0d).RechargeReadyAt, Is.EqualTo(new[] { 10d }));
            restored.Refresh(10d);
            Assert.That(restored.AvailableCharges, Is.EqualTo(2));
            Assert.That(source.AvailableCharges, Is.EqualTo(1), "Property reads do not advance the other runtime's clock.");
        }

        [Test]
        public void OwnerPredictionAndServerPermissionUseIndependentRuntimeInstancesOnHost()
        {
            var owner = new PlayerDashRuntime(2);
            var server = new PlayerDashRuntime(2);
            Assert.That(owner.TryConsume(4d, 0.2d, 3d), Is.True);
            Assert.That(server.AvailableCharges, Is.EqualTo(2));
            Assert.That(server.TryConsume(4d, 0.2d, 3d), Is.True);
            Assert.That(owner.AvailableCharges, Is.EqualTo(1));
            Assert.That(server.AvailableCharges, Is.EqualTo(1));
            owner.Restore(server.Capture(4d), 4d);
            Assert.That(owner.AvailableCharges, Is.EqualTo(1), "Applying the acknowledgement is a restore, never another consumption.");
        }

        [Test]
        public void InvalidConsumesDoNotRefreshChargesOrChangeAnyDeadline()
        {
            var runtime = new PlayerDashRuntime(2);
            runtime.TryConsume(0d, 0d, 1d);
            foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Assert.That(runtime.TryConsume(invalid, 0d, 1d), Is.False);
                Assert.That(runtime.TryConsume(2d, invalid, 1d), Is.False);
                Assert.That(runtime.TryConsume(2d, 0d, invalid), Is.False);
            }
            Assert.That(runtime.TryConsume(double.MaxValue, double.MaxValue, 1d), Is.False);
            Assert.That(runtime.TryConsume(double.MaxValue, 0d, double.MaxValue), Is.False);
            Assert.That(runtime.AvailableCharges, Is.EqualTo(1));
            Assert.That(runtime.NextUseAt, Is.EqualTo(0.15d));
        }

        [Test]
        public void InvalidRestoreIsAtomicEvenWhenValidDeadlinesPrecedeAnInvalidEntry()
        {
            var runtime = new PlayerDashRuntime(2);
            runtime.TryConsume(1d, 0.2d, 10d);
            PlayerDashSnapshot before = runtime.Capture(1d);
            var invalid = new PlayerDashSnapshot { MaxCharges = 9, NextUseAt = 2d, RechargeReadyAt = new[] { 4d, double.NaN } };
            Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Restore(invalid, 1d));
            invalid = new PlayerDashSnapshot { MaxCharges = -1, NextUseAt = 2d, RechargeReadyAt = Array.Empty<double>() };
            Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Restore(invalid, 1d));
            invalid = new PlayerDashSnapshot { MaxCharges = 9, NextUseAt = double.PositiveInfinity };
            Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Restore(invalid, 1d));
            Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Restore(default, double.NaN));
            PlayerDashSnapshot after = runtime.Capture(1d);
            Assert.That(after.MaxCharges, Is.EqualTo(before.MaxCharges));
            Assert.That(after.NextUseAt, Is.EqualTo(before.NextUseAt));
            Assert.That(after.RechargeReadyAt, Is.EqualTo(before.RechargeReadyAt));
        }

        [Test]
        public void ZeroCapacityDefaultSnapshotAndZeroRechargeHaveExplicitBehavior()
        {
            var runtime = new PlayerDashRuntime();
            Assert.That(runtime.TryConsume(0d, 0d, 0d), Is.False);
            runtime.Restore(default, 0d);
            Assert.That(runtime.Capture(0d).RechargeReadyAt, Is.Empty);
            runtime.ConfigureCapacity(1);
            Assert.That(runtime.TryConsume(0d, 0d, 0d), Is.True);
            Assert.That(runtime.AvailableCharges, Is.Zero);
            runtime.Refresh(0d);
            Assert.That(runtime.AvailableCharges, Is.EqualTo(1));
            Assert.That(runtime.TryConsume(0d, 0d, 0d), Is.False);
            Assert.That(runtime.TryConsume(0.15d, 0d, 0d), Is.True);
        }

        [Test]
        public void NegativeCapacityAndInvalidExplicitClockReadsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerDashRuntime(-1));
            var runtime = new PlayerDashRuntime(2);
            Assert.Throws<ArgumentOutOfRangeException>(() => runtime.ConfigureCapacity(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Refresh(-1d));
            Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Capture(double.NaN));
            Assert.That(runtime.MaxCharges, Is.EqualTo(2));
        }
    }
}
