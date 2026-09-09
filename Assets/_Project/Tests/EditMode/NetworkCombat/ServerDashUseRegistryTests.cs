using System;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class ServerDashUseRegistryTests
    {
        [Test]
        public void AcceptedUsesSpendCanonicalChargesAndRejectedExhaustionAddsNoPermit()
        {
            ServerDashUseRegistry registry = Create(2);
            Assert.That(registry.TryConsume(Id(1), 3, 0d, 0.2f, 2f, Weapons()), Is.True);
            Assert.That(registry.TryConsume(Id(2), 3, 0.4d, 0.2f, 2f, Weapons()), Is.True);
            Assert.That(registry.Runtime.AvailableCharges, Is.Zero);
            Assert.That(registry.PendingUseCount, Is.EqualTo(2));
            double nextUseAt = registry.Runtime.NextUseAt;
            Assert.That(registry.TryConsume(Id(3), 3, 1d, 0.2f, 2f, Weapons()), Is.False);
            Assert.That(registry.Runtime.NextUseAt, Is.EqualTo(nextUseAt));
            Assert.That(registry.PendingUseCount, Is.EqualTo(2));
            Assert.That(registry.Runtime.Refresh(2d), Is.True);
            Assert.That(registry.Runtime.AvailableCharges, Is.EqualTo(1));
        }

        [Test]
        public void ReceiptJitterUsesTheExistingDeadlineWithoutShorteningTheNextInterval()
        {
            ServerDashUseRegistry registry = Create(3);
            Assert.That(registry.TryConsume(Id(1), 1, 0d, 0.2f, 5f, Weapons()), Is.True);
            double firstGate = registry.Runtime.NextUseAt;
            Assert.That(registry.TryConsume(Id(2), 1, firstGate - 0.06d, 0.2f, 5f, Weapons()), Is.False);
            Assert.That(registry.TryConsume(Id(2), 1, firstGate - 0.04d, 0.2f, 5f, Weapons()), Is.True);
            Assert.That(registry.Runtime.NextUseAt, Is.EqualTo(firstGate + 0.2f + 0.15d).Within(0.000001d));
            PlayerDashSnapshot snapshot = registry.Runtime.Capture(firstGate);
            Assert.That(snapshot.RechargeReadyAt[1], Is.EqualTo(firstGate + 5d).Within(0.000001d));
            Assert.That(registry.PendingUseCount, Is.EqualTo(2));
        }

        [Test]
        public void AUsePermitsEachCapturedWeaponSlotOnceEvenWhenTwoSlotsShareTheSameDefinition()
        {
            ServerDashUseRegistry registry = Create(2);
            uint[] weapons = Weapons();
            Assert.That(registry.TryConsume(Id(1), 5, 10d, 0.2f, 2f, weapons), Is.True);
            Assert.That(registry.CanAdmitWeapon(Id(1), 5, 0, 4, 10d), Is.True);
            Assert.That(registry.CanAdmitWeapon(Id(1), 5, 1, 4, 10d), Is.True);
            registry.MarkWeaponAdmitted(Id(1), 0);
            Assert.That(registry.CanAdmitWeapon(Id(1), 5, 0, 4, 10d), Is.False);
            Assert.That(registry.CanAdmitWeapon(Id(1), 5, 1, 4, 10d), Is.True);
            registry.MarkWeaponAdmitted(Id(1), 1);
            Assert.That(registry.CanAdmitWeapon(Id(1), 5, 1, 4, 10d), Is.False);
            Assert.That(registry.CanAdmitWeapon(Id(1), 5, 3, 6, 10d), Is.True);
            Assert.That(registry.Runtime.AvailableCharges, Is.EqualTo(1), "Weapon permits do not consume another dash charge.");
        }

        [Test]
        public void BuildRevisionAndCapturedWeaponMappingCannotBeChangedAfterTheUseWasAccepted()
        {
            ServerDashUseRegistry registry = Create(1);
            uint[] weapons = Weapons();
            registry.TryConsume(Id(1), 5, 10d, 0.2f, 2f, weapons);
            weapons[0] = 99;
            Assert.That(registry.CanAdmitWeapon(Id(1), 5, 0, 4, 10d), Is.True);
            Assert.That(registry.CanAdmitWeapon(Id(1), 6, 0, 4, 10d), Is.False);
            Assert.That(registry.CanAdmitWeapon(Id(1), 5, 0, 99, 10d), Is.False);
            Assert.That(registry.CanAdmitWeapon(Id(1), 5, 2, 0, 10d), Is.False);
            Assert.That(registry.CanAdmitWeapon(Id(1), 5, -1, 4, 10d), Is.False);
            Assert.That(registry.CanAdmitWeapon(Id(1), 5, PlayerBuildRuntime.HandSlotCount, 4, 10d), Is.False);
            Assert.That(registry.CanAdmitWeapon(Id(99), 5, 0, 4, 10d), Is.False);
            Assert.That(registry.CanAdmitWeapon(Id(1), 5, 0, 4, 10d), Is.True, "Failed checks do not consume the valid slot.");
        }

        [Test]
        public void ExpiredPermitsCannotAdmitWeaponsOrReplayAnAlreadyAcceptedUse()
        {
            ServerDashUseRegistry registry = Create(2);
            registry.TryConsume(Id(8), 1, 10d, 0.2f, 1f, Weapons());
            Assert.That(registry.CanAdmitWeapon(Id(8), 1, 0, 4, 12d), Is.True);
            Assert.That(registry.CanAdmitWeapon(Id(8), 1, 0, 4, 12.001d), Is.False);
            Assert.That(registry.PendingUseCount, Is.Zero);
            Assert.That(registry.TryConsume(Id(8), 1, 20d, 0.2f, 1f, Weapons()), Is.False);
            Assert.That(registry.TryConsume(Id(7), 1, 20d, 0.2f, 1f, Weapons()), Is.False);
            Assert.That(registry.TryConsume(Id(9), 1, 20d, 0.2f, 1f, Weapons()), Is.True);
        }

        [Test]
        public void ClearingPermitsDoesNotRefundChargesResetTheGateOrForgetReplayProtection()
        {
            ServerDashUseRegistry registry = Create(2);
            registry.TryConsume(Id(1), 1, 10d, 0.2f, 5f, Weapons());
            PlayerDashSnapshot before = registry.Runtime.Capture(10d);
            registry.ClearPermits();
            Assert.That(registry.PendingUseCount, Is.Zero);
            Assert.That(registry.CanAdmitWeapon(Id(1), 1, 0, 4, 10d), Is.False);
            Assert.That(registry.TryConsume(Id(1), 2, 16d, 0.2f, 5f, Weapons()), Is.False);
            PlayerDashSnapshot after = registry.Runtime.Capture(10d);
            Assert.That(after.NextUseAt, Is.EqualTo(before.NextUseAt));
            Assert.That(after.RechargeReadyAt, Is.EqualTo(before.RechargeReadyAt));
        }

        [Test]
        public void InvalidClockCannotAdmitAnExpiredUseOrDestroyOtherwiseLivePermits()
        {
            ServerDashUseRegistry registry = Create(2);
            registry.TryConsume(Id(1), 1, 10d, 0.2f, 5f, Weapons());
            foreach (double now in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Assert.That(registry.CanAdmitWeapon(Id(1), 1, 0, 4, now), Is.False);
                registry.Prune(now);
                Assert.That(registry.PendingUseCount, Is.EqualTo(1));
            }
            Assert.That(registry.CanAdmitWeapon(Id(1), 1, 0, 4, 10d), Is.True);
        }

        [Test]
        public void InvalidUseDataCannotConsumeCanonicalCharges()
        {
            ServerDashUseRegistry registry = Create(2);
            ulong zeroSequence = (3UL << 48) | (7UL << 32);
            Assert.That(registry.TryConsume(0, 1, 0d, 0.2f, 2f, Weapons()), Is.False);
            Assert.That(registry.TryConsume(zeroSequence, 1, 0d, 0.2f, 2f, Weapons()), Is.False);
            Assert.That(registry.TryConsume(Id(1), 0, 0d, 0.2f, 2f, Weapons()), Is.False);
            Assert.That(registry.TryConsume(Id(1), 1, 0d, 0.2f, 2f, null), Is.False);
            Assert.That(registry.TryConsume(Id(1), 1, 0d, 0.2f, 2f, new uint[3]), Is.False);
            foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
            {
                Assert.That(registry.TryConsume(Id(1), 1, invalid, 0.2f, 2f, Weapons()), Is.False);
                Assert.That(registry.TryConsume(Id(1), 1, 0d, invalid, 2f, Weapons()), Is.False);
                Assert.That(registry.TryConsume(Id(1), 1, 0d, 0.2f, invalid, Weapons()), Is.False);
            }
            Assert.That(registry.Runtime.AvailableCharges, Is.EqualTo(2));
            Assert.That(registry.Runtime.NextUseAt, Is.Zero);
            Assert.That(registry.PendingUseCount, Is.Zero);
        }

        [Test]
        public void ReconnectedAvatarRestoresOfflineDeadlinesButNoOldWeaponPermits()
        {
            ServerDashUseRegistry original = Create(2);
            original.TryConsume(Id(50), 1, 10d, 0.2f, 2f, Weapons());
            original.TryConsume(Id(51), 1, 10.5d, 0.2f, 8f, Weapons());
            PlayerDashSnapshot saved = original.Runtime.Capture(11d);
            ServerDashUseRegistry restored = Create(0);
            restored.Runtime.Restore(saved, 14d);
            Assert.That(restored.Runtime.AvailableCharges, Is.EqualTo(1));
            Assert.That(restored.PendingUseCount, Is.Zero);
            Assert.That(restored.CanAdmitWeapon(Id(51), 1, 0, 4, 14d), Is.False);
            ulong reconnected = CombatEventId.Compose(3, 8, 1).Value;
            Assert.That(restored.TryConsume(reconnected, 1, 14d, 0.2f, 5f, Weapons()), Is.True);
            Assert.That(restored.Runtime.AvailableCharges, Is.Zero);
            Assert.That(restored.Runtime.Capture(14d).RechargeReadyAt, Is.EqualTo(new[] { 18.5d, 19d }));
            saved.RechargeReadyAt[1] = 0d;
            Assert.That(restored.Runtime.AvailableCharges, Is.Zero);
        }

        [Test]
        public void MarkingAnUnknownUseOrOutOfRangeSlotIsRejected()
        {
            ServerDashUseRegistry registry = Create(1);
            Assert.Throws<InvalidOperationException>(() => registry.MarkWeaponAdmitted(Id(1), 0));
            registry.TryConsume(Id(1), 1, 0d, 0.2f, 5f, Weapons());
            Assert.Throws<InvalidOperationException>(() => registry.MarkWeaponAdmitted(Id(1), -1));
            Assert.Throws<InvalidOperationException>(() => registry.MarkWeaponAdmitted(Id(1), PlayerBuildRuntime.HandSlotCount));
        }

        private static ServerDashUseRegistry Create(int capacity)
        {
            var registry = new ServerDashUseRegistry();
            registry.Runtime.ConfigureCapacity(capacity);
            return registry;
        }
        private static uint[] Weapons() => new uint[] { 4, 4, 0, 6 };
        private static ulong Id(uint sequence) => CombatEventId.Compose(3, 7, sequence).Value;
    }
}
