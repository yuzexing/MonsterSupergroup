using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class PlayerWeaponCooldownTests
    {
        [Test]
        public void CurrentConnectionIdentityRejectsOtherPlayerAndPreviousEpoch()
        {
            var identities = new ClientEventIdentityRegistry();
            identities.Register(10, 3, 7);
            identities.Register(20, 4, 7);
            CombatEventId current = CombatEventId.Compose(3, 7, 8);
            Assert.That(identities.Validate(10, current.Value, current.Sequence), Is.True);
            Assert.That(identities.Validate(20, current.Value, current.Sequence), Is.False);
            Assert.That(identities.Validate(10, current.Value, current.Sequence + 1), Is.False);

            // A reconnect must register its new identity before accepting owner timing.
            identities.Unregister(10);
            identities.Register(11, 3, 8);
            Assert.That(identities.Validate(11, current.Value, current.Sequence), Is.False);
            CombatEventId reconnected = CombatEventId.Compose(3, 8, 1);
            Assert.That(identities.Validate(11, reconnected.Value, 1), Is.True);
        }

        [TestCase(90d, 0d)]
        [TestCase(98d, 2d)]
        [TestCase(100d, 4d)]
        [TestCase(200d, 4d)]
        public void OwnerTimeIsBoundedByServerClockAndCurrentWeaponCooldown(double ownerTime, double remaining)
        {
            Assert.That(PlayerWeaponCooldownSnapshot.TryObserve(0, 2, Id(1), ownerTime, 100, 4,
                default, out var observed), Is.True);
            Assert.That(observed.IsValid, Is.True);
            Assert.That(observed.ServerReceivedAt, Is.EqualTo(100));
            Assert.That(observed.RemainingAt(100), Is.EqualTo(remaining));
            Assert.That(observed.RemainingAt(110), Is.Zero);
        }

        [Test]
        public void InvalidSlotsIdsAndNonFiniteTimingCannotBecomeSavedCooldown()
        {
            AssertInvalid(-1, 2, Id(1), 100, 100, 4);
            AssertInvalid(PlayerBuildRuntime.HandSlotCount, 2, Id(1), 100, 100, 4);
            AssertInvalid(0, 0, Id(1), 100, 100, 4);
            AssertInvalid(0, 2, 0, 100, 100, 4);
            AssertInvalid(0, 2, (3ul << 48) | (7ul << 32), 100, 100, 4);
            foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                AssertInvalid(0, 2, Id(1), value, 100, 4);
                AssertInvalid(0, 2, Id(1), 100, value, 4);
                AssertInvalid(0, 2, Id(1), 100, 100, (float)value);
            }
            AssertInvalid(0, 2, Id(1), 100, 100, 0);
            AssertInvalid(0, 2, Id(1), 100, 100, -1);
        }

        [Test]
        public void DuplicateAndOlderSequenceDoNotReplaceSavedDeadline()
        {
            Assert.That(PlayerWeaponCooldownSnapshot.TryObserve(0, 2, Id(8), 100, 100, 4,
                default, out var saved), Is.True);
            foreach (uint sequence in new[] { 8u, 7u, 1u })
                Assert.That(PlayerWeaponCooldownSnapshot.TryObserve(0, 2, Id(sequence), 104, 104, 4,
                    saved, out _), Is.False);
            Assert.That(saved.ReadyAt, Is.EqualTo(104));
            Assert.That(saved.LastAttackEventId, Is.EqualTo(Id(8)));
        }

        [Test]
        public void NewerSequenceWithOldTimestampCannotShortenUnexpiredCooldown()
        {
            PlayerWeaponCooldownSnapshot.TryObserve(0, 2, Id(8), 100, 100, 4, default, out var saved);
            Assert.That(PlayerWeaponCooldownSnapshot.TryObserve(0, 2, Id(9), 1, 101, 4,
                saved, out var observed), Is.True);
            Assert.That(observed.ReadyAt, Is.EqualTo(104));
            Assert.That(observed.RemainingAt(101), Is.EqualTo(3));
        }

        [Test]
        public void TwoWeaponSlotsKeepIndependentDeadlinesEvenWithTheSameDefinition()
        {
            PlayerWeaponCooldownSnapshot.TryObserve(0, 2, Id(1), 100, 100, 4, default, out var first);
            PlayerWeaponCooldownSnapshot.TryObserve(2, 2, Id(2), 101, 101, 4, default, out var second);
            Assert.That(first.SlotIndex, Is.Zero);
            Assert.That(second.SlotIndex, Is.EqualTo(2));
            Assert.That(first.RemainingAt(102), Is.EqualTo(2));
            Assert.That(second.RemainingAt(102), Is.EqualTo(3));
        }

        [Test]
        public void ReconnectedEpochStartsItsSequenceWithoutReplayingTheOldDeadline()
        {
            PlayerWeaponCooldownSnapshot.TryObserve(0, 2, Id(50), 100, 100, 4, default, out var saved);
            ulong newId = CombatEventId.Compose(3, 8, 1).Value;
            Assert.That(PlayerWeaponCooldownSnapshot.TryObserve(0, 2, newId, 102, 102, 4,
                saved, out var observed), Is.True);
            Assert.That(observed.LastAttackEventId, Is.EqualTo(newId));
            Assert.That(observed.ReadyAt, Is.EqualTo(106));
            Assert.That(saved.ReadyAt, Is.EqualTo(104));
        }

        private static ulong Id(uint sequence) => CombatEventId.Compose(3, 7, sequence).Value;

        private static void AssertInvalid(int slot, uint weapon, ulong id, double ownerTime,
            double serverTime, float cooldown)
        {
            Assert.That(PlayerWeaponCooldownSnapshot.TryObserve(slot, weapon, id, ownerTime,
                serverTime, cooldown, default, out var snapshot), Is.False);
            Assert.That(snapshot.IsValid, Is.False);
        }
    }
}
