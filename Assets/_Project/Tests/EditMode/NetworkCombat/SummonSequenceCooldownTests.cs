using MonsterSupergroup.GAS;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class SummonSequenceCooldownTests
    {
        private static readonly ulong Root = CombatEventId.Compose(7, 2, 4).Value;

        [Test]
        public void EarlyTargetLossShortensOnlySequenceAndPreservesFullOrdinaryCooldown()
        {
            PlayerWeaponCooldownSnapshot original = Snapshot();
            Assert.That(original.TryCompleteSequence(Root, 102d, 1.6333334f, out var completed), Is.True);
            Assert.That(completed.SequenceSeconds, Is.EqualTo(2f));
            Assert.That(completed.ReadyAt, Is.EqualTo(107d));
            Assert.That(completed.CooldownSeconds, Is.EqualTo(5f));
            Assert.That(completed.ServerReceivedAt, Is.EqualTo(original.ServerReceivedAt));
            Assert.That(completed.LastAttackEventId, Is.EqualTo(Root));
            Assert.That(completed.SlotIndex, Is.EqualTo(2));
            Assert.That(completed.WeaponId, Is.EqualTo(402));
            Assert.That(original.ReadyAt, Is.EqualTo(115d), "Completing a copy cannot mutate an already captured checkpoint.");
            Assert.That(completed.RemainingAt(106d), Is.EqualTo(1d));
            Assert.That(completed.RemainingAt(108d), Is.Zero, "Offline session time consumes the remaining cooldown.");
        }

        [Test]
        public void NaturalCompletionCannotSkipEnterAndExitButCancellationHasNoMinimumSequence()
        {
            var original = Snapshot();
            const float minimum = 1.6333334f;
            Assert.That(original.TryCompleteSequence(Root, 100.1d, minimum, out var natural), Is.True);
            Assert.That(natural.SequenceSeconds, Is.EqualTo(minimum).Within(0.000001f));
            Assert.That(natural.ReadyAt, Is.EqualTo(105d + minimum).Within(0.000001d));
            Assert.That(original.TryCompleteSequence(Root, 100.1d, 0f, out var cancelled), Is.True);
            Assert.That(cancelled.SequenceSeconds, Is.EqualTo(0.1f).Within(0.000001f));
            Assert.That(cancelled.ReadyAt, Is.EqualTo(105.1d).Within(0.000001d));
        }

        [Test]
        public void ActualLongSequenceAndChangedAttackSpeedKeepTheirCurrentFullCooldown()
        {
            var original = Snapshot().WithCooldown(2f);
            Assert.That(original.TryCompleteSequence(Root, 113d, 1.6333334f, out var completed), Is.True);
            Assert.That(completed.SequenceSeconds, Is.EqualTo(13f));
            Assert.That(completed.ReadyAt, Is.EqualTo(115d));
            Assert.That(completed.CooldownSeconds, Is.EqualTo(2f));
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(2, 402, CombatEventId.Compose(7, 2, 5).Value,
                114.5d, 114.5d, 2f, completed, out _, 2.6333334f), Is.False);
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(2, 402, CombatEventId.Compose(7, 2, 5).Value,
                115d, 115d, 2f, completed, out _, 2.6333334f), Is.True);
        }

        [Test]
        public void InvalidInputWrongRootAndLegacyCheckpointCannotRewriteTheSavedDeadline()
        {
            var original = Snapshot();
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Assert.That(original.TryCompleteSequence(Root, invalid, 0f, out var unchanged), Is.False);
                Assert.That(unchanged, Is.EqualTo(original));
            }
            foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Assert.That(original.TryCompleteSequence(Root, 102d, invalid, out var unchanged), Is.False);
                Assert.That(unchanged, Is.EqualTo(original));
            }
            Assert.That(original.TryCompleteSequence(Root + 1, 102d, 0f, out var wrongRoot), Is.False);
            Assert.That(wrongRoot, Is.EqualTo(original));
            Assert.That(original.TryCompleteSequence(Root, double.MaxValue, 0f, out var overflow), Is.False);
            Assert.That(overflow, Is.EqualTo(original));
            original.CooldownSeconds = 0f;
            Assert.That(original.TryCompleteSequence(Root, 102d, 0f, out var legacy), Is.False);
            Assert.That(legacy, Is.EqualTo(original));
            original = Snapshot(); original.SlotIndex = 4;
            Assert.That(original.TryCompleteSequence(Root, 102d, 0f, out var invalidSnapshot), Is.False);
            Assert.That(invalidSnapshot, Is.EqualTo(original));
        }

        private static PlayerWeaponCooldownSnapshot Snapshot() => new PlayerWeaponCooldownSnapshot
        {
            SlotIndex = 2, WeaponId = 402, LastAttackEventId = Root,
            ServerReceivedAt = 100.04d, ReadyAt = 115d, CooldownSeconds = 5f, SequenceSeconds = 10f
        };
    }
}
