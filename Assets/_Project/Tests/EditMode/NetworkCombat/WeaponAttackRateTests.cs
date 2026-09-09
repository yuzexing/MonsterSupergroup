using MonsterSupergroup.GAS;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class WeaponAttackRateTests
    {
        [Test]
        public void ThreeSlashRootChargesItsLaunchSequenceBeforeNormalCooldown()
        {
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(0, 1, CombatEventId.Compose(3, 7, 1).Value,
                100, 100, 1, default, out var saved, 0.3f), Is.True);
            Assert.That(saved.CooldownSeconds, Is.EqualTo(1));
            Assert.That(saved.SequenceSeconds, Is.EqualTo(0.3f));
            Assert.That(saved.ReadyAt, Is.EqualTo(101.3).Within(0.000001));
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(0, 1, CombatEventId.Compose(3, 7, 2).Value,
                101, 101, 1, saved, out _, 0.3f), Is.False);
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(0, 1, CombatEventId.Compose(3, 7, 2).Value,
                101.3, 101.3, 1, saved, out var next, 0.3f), Is.True);
            Assert.That(next.ReadyAt, Is.EqualTo(102.6).Within(0.000001));
        }

        [Test]
        public void ChangedSlashCountOnlyAffectsNextRootWhileSpeedNormalizesOnce()
        {
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(0, 1, CombatEventId.Compose(3, 7, 1).Value,
                100, 100, 1, default, out var saved, 0.3f), Is.True);
            var faster = saved.WithCooldown(0.5f);
            Assert.That(faster.ReadyAt, Is.EqualTo(100.8).Within(0.000001));
            Assert.That(faster.SequenceSeconds, Is.EqualTo(0.3f));
            Assert.That(faster.WithCooldown(0.5f).ReadyAt, Is.EqualTo(faster.ReadyAt));
            Assert.That(faster.WithCooldown(1).ReadyAt, Is.EqualTo(saved.ReadyAt));
            ulong nextRoot = CombatEventId.Compose(3, 8, 1).Value;
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(0, 1, nextRoot,
                100.5, 100.5, 0.5f, faster, out _, 0.7f), Is.False);
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(0, 1, nextRoot,
                100.8, 100.8, 0.5f, faster, out var next, 0.7f), Is.True);
            Assert.That(next.ReadyAt, Is.EqualTo(102).Within(0.000001));
            Assert.That(next.SequenceSeconds, Is.EqualTo(0.7f));
            Assert.That(faster.SequenceSeconds, Is.EqualTo(0.3f), "An already launched root keeps its original count's duration.");
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void InvalidSequenceDurationCannotBeAdmittedOrRestored(float duration)
        {
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(0, 1, CombatEventId.Compose(3, 7, 1).Value,
                100, 100, 1, default, out var invalid, duration), Is.False);
            Assert.That(invalid.IsValid, Is.False);
            Assert.That(Admit(1, 100, 100, 1, default, out var saved), Is.True);
            saved.SequenceSeconds = duration;
            Assert.That(saved.IsValid, Is.False);
        }

        [Test]
        public void OldCheckpointWithoutSequenceRestoresAsInstantLaunch()
        {
            var old = UnityEngine.JsonUtility.FromJson<PlayerWeaponCooldownSnapshot>(
                "{\"SlotIndex\":0,\"WeaponId\":2,\"LastAttackEventId\":1,\"ServerReceivedAt\":100,\"ReadyAt\":101,\"CooldownSeconds\":1}");
            Assert.That(old.IsValid, Is.True);
            Assert.That(old.SequenceSeconds, Is.Zero);
            Assert.That(old.WithCooldown(0.5f).ReadyAt, Is.EqualTo(100.5));
        }

        [Test]
        public void BurstWithFreshIds_CannotSkipTheWeaponCooldown()
        {
            Assert.That(Admit(1, 100, 100, 1, default, out var saved), Is.True);
            for (uint sequence = 2; sequence <= 10; sequence++)
                Assert.That(Admit(sequence, 100.01, 100.01, 1, saved, out _), Is.False);

            Assert.That(saved.ReadyAt, Is.EqualTo(101));
            Assert.That(Admit(11, 101, 101, 1, saved, out var next), Is.True);
            Assert.That(next.ReadyAt, Is.EqualTo(102));
        }

        [Test]
        public void JitterTolerance_ChargesFullIntervalsInsteadOfAccumulatingFasterRate()
        {
            Assert.That(Admit(1, 100, 100, 1, default, out var saved), Is.True);
            int accepted = 1;
            for (uint sequence = 2; sequence <= 30; sequence++)
            {
                double time = 100 + (sequence - 1) * 0.92;
                if (!Admit(sequence, time, time, 1, saved, out var next)) continue;
                saved = next;
                accepted++;
                Assert.That(saved.ReadyAt, Is.GreaterThanOrEqualTo(100 + accepted - 0.000001),
                    "Each accepted attack owes a full interval, including attacks admitted slightly early.");
            }

            Assert.That(accepted, Is.GreaterThan(1), "The tolerance must still allow attacks while time advances.");
            Assert.That(accepted, Is.LessThan(30), "Repeated early inputs cannot all be admitted.");
        }

        [Test]
        public void LegalOwnerIntervals_AreAcceptedWhenReliableMessagesArriveTogether()
        {
            Assert.That(Admit(1, 100, 102, 1, default, out var saved), Is.True);
            Assert.That(saved.ReadyAt, Is.EqualTo(101));
            Assert.That(Admit(2, 101, 102, 1, saved, out var second), Is.True);
            Assert.That(Admit(3, 102, 102, 1, second, out var third), Is.True);

            Assert.That(second.ReadyAt, Is.EqualTo(102));
            Assert.That(third.ReadyAt, Is.EqualTo(103));
            Assert.That(third.ServerReceivedAt, Is.EqualTo(102));
        }

        [TestCase(97.99d)]
        [TestCase(100.11d)]
        [TestCase(double.NaN)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(double.PositiveInfinity)]
        public void UnboundedOwnerTimestamp_CannotCreateAnAdmission(double ownerTime)
        {
            Assert.That(Admit(1, ownerTime, 100, 1, default, out var saved), Is.False);
            Assert.That(saved.IsValid, Is.False);
        }

        [Test]
        public void SmallPositiveClockOffset_IsClampedToServerTime()
        {
            Assert.That(Admit(1, 100.05, 100, 1, default, out var saved), Is.True);
            Assert.That(saved.ReadyAt, Is.EqualTo(101));
        }

        [Test]
        public void SpeedUpgrade_RecomputesTheExistingIntervalUsingNewCooldown()
        {
            Assert.That(Admit(1, 100, 100, 4, default, out var saved), Is.True);
            Assert.That(saved.ReadyAt, Is.EqualTo(104));
            Assert.That(Admit(2, 100.5, 100.5, 1, saved, out _), Is.False);
            Assert.That(Admit(3, 101, 101, 1, saved, out var upgraded), Is.True);

            Assert.That(upgraded.ReadyAt, Is.EqualTo(102));
            Assert.That(upgraded.CooldownSeconds, Is.EqualTo(1));
        }

        [Test]
        public void SlowerWeaponStats_ExtendTheExistingInterval()
        {
            Assert.That(Admit(1, 100, 100, 1, default, out var saved), Is.True);
            Assert.That(Admit(2, 102, 102, 4, saved, out _), Is.False);
            Assert.That(Admit(3, 104, 104, 4, saved, out var slower), Is.True);

            Assert.That(slower.ReadyAt, Is.EqualTo(108));
            Assert.That(slower.CooldownSeconds, Is.EqualTo(4));
        }

        [TestCase(4f, 1f)]
        [TestCase(1f, 4f)]
        public void CaptureNormalizationAndAdmissionUseTheSameDeadline(float previousCooldown, float currentCooldown)
        {
            Assert.That(Admit(1, 100, 100, previousCooldown, default, out var saved), Is.True);
            var normalized = saved.WithCooldown(currentCooldown);
            double readyAt = 100 + currentCooldown;

            Assert.That(normalized.ReadyAt, Is.EqualTo(readyAt));
            Assert.That(normalized.RemainingAt(100), Is.EqualTo(currentCooldown));
            Assert.That(normalized.WithCooldown(currentCooldown).ReadyAt, Is.EqualTo(readyAt),
                "Repeated capture cannot apply the same speed change twice.");
            Assert.That(normalized.WithCooldown(previousCooldown).ReadyAt, Is.EqualTo(saved.ReadyAt));
            Assert.That(normalized.LastAttackEventId, Is.EqualTo(saved.LastAttackEventId));
            Assert.That(normalized.ServerReceivedAt, Is.EqualTo(saved.ServerReceivedAt));
            Assert.That(Admit(2, readyAt - currentCooldown * 0.25, readyAt - currentCooldown * 0.25,
                currentCooldown, normalized, out _), Is.False);
            Assert.That(Admit(2, readyAt, readyAt, currentCooldown, normalized, out var fromCapture), Is.True);
            Assert.That(Admit(2, readyAt, readyAt, currentCooldown, saved, out var directlyAdmitted), Is.True);
            Assert.That(fromCapture.ReadyAt, Is.EqualTo(directlyAdmitted.ReadyAt));
        }

        [Test]
        public void ReconnectNewEpoch_RetainsUnexpiredSavedCooldown()
        {
            Assert.That(Admit(50, 100, 100, 4, default, out var saved), Is.True);
            ulong reconnectedId = CombatEventId.Compose(3, 8, 1).Value;
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(0, 2, reconnectedId,
                102, 102, 4, saved, out _), Is.False);
            Assert.That(saved.ReadyAt, Is.EqualTo(104));
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(0, 2, reconnectedId,
                104, 104, 4, saved, out var next), Is.True);

            Assert.That(next.ReadyAt, Is.EqualTo(108));
            Assert.That(next.LastAttackEventId, Is.EqualTo(reconnectedId));
        }

        [Test]
        public void OldCheckpointWithoutInterval_KeepsItsSavedDeadline()
        {
            Assert.That(Admit(50, 100, 100, 4, default, out var saved), Is.True);
            saved.CooldownSeconds = 0;
            var normalized = saved.WithCooldown(1).WithCooldown(2);
            Assert.That(normalized.ReadyAt, Is.EqualTo(saved.ReadyAt));
            Assert.That(normalized.CooldownSeconds, Is.Zero,
                "A missing historical interval cannot be inferred during checkpoint capture.");
            ulong reconnectedId = CombatEventId.Compose(3, 8, 1).Value;
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(0, 2, reconnectedId,
                102, 102, 1, saved, out _), Is.False);
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(0, 2, reconnectedId,
                104, 104, 1, saved, out var next), Is.True);

            Assert.That(next.ReadyAt, Is.EqualTo(105));
        }

        [Test]
        public void DuplicateRoot_CannotBecomeAnotherLegalAttackAfterTimePasses()
        {
            Assert.That(Admit(10, 100, 100, 1, default, out var saved), Is.True);
            Assert.That(Admit(10, 101, 101, 1, saved, out _), Is.False);
            Assert.That(Admit(9, 102, 102, 1, saved, out _), Is.False);
            Assert.That(Admit(11, 103, 103, 1, saved, out var next), Is.True);
            Assert.That(next.ReadyAt, Is.EqualTo(104));
        }

        private static bool Admit(uint sequence, double ownerTime, double serverTime,
            float cooldown, PlayerWeaponCooldownSnapshot previous,
            out PlayerWeaponCooldownSnapshot snapshot) =>
            PlayerWeaponCooldownSnapshot.TryAdmit(0, 2, CombatEventId.Compose(3, 7, sequence).Value,
                ownerTime, serverTime, cooldown, previous, out snapshot);
    }
}
