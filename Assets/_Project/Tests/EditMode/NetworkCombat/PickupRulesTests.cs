using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class PickupRulesTests
    {
        private static PickupDropSchedule Schedule() => new PickupDropSchedule(
            new ExperienceParameters(AnimationCurve.Linear(0, 0, 1, 1), 2, .5f), .001f, .1f);

        [Test]
        public void XpSequenceIsUnchanged_AndOnlyNonXpDeathsRollItems()
        {
            var schedule = Schedule(); int rolls = 0;
            var expected = new[] { PickupEffect.Experience, PickupEffect.Experience, PickupEffect.Experience,
                PickupEffect.Experience, PickupEffect.RestoreHealth, PickupEffect.Experience };
            for (uint i = 1; i <= 6; i++)
                Assert.That(schedule.Consume(i, 2, 7, 1, 0, 4, () => { rolls++; return 0; }).Effect,
                    Is.EqualTo(expected[i - 1]));
            Assert.That(rolls, Is.EqualTo(1));
        }

        [Test]
        public void DuplicateDoesNotAdvanceEitherAccumulator_AndZeroXpDoesNotFallback()
        {
            var schedule = Schedule();
            Assert.That(schedule.Consume(1, 2, 0, 0, 0, 4, () => 0).Reason, Is.EqualTo("nonpositive-xp"));
            float xp = schedule.XpAccumulator, item = schedule.ItemAccumulator;
            Assert.That(schedule.Consume(1, 2, 8, 0, 0, 4, () => 0).Reason, Is.EqualTo("duplicate-death"));
            Assert.That(schedule.XpAccumulator, Is.EqualTo(xp));
            Assert.That(schedule.ItemAccumulator, Is.EqualTo(item));
        }

        [Test]
        public void ItemSuccessAtCapConsumesChanceWithoutRetryOrXpFallback()
        {
            var schedule = Schedule();
            for (uint i = 1; i <= 4; i++) schedule.Consume(i, 2, 7, 1, 4, 4, () => 1);
            var decision = schedule.Consume(5, 2, 7, 0, 4, 4, () => .105f);
            Assert.That(decision.Reason, Is.EqualTo("item-cap"));
            Assert.That(decision.Effect, Is.EqualTo(PickupEffect.None));
            Assert.That(schedule.ItemAccumulator, Is.Zero);
            Assert.That(schedule.Consume(6, 2, 7, 0, 0, 4, () => 0).Effect, Is.EqualTo(PickupEffect.Experience));
        }

        [TestCase(0f, .104f, PickupEffect.RestoreHealth)]
        [TestCase(1f, .006f, PickupEffect.None)]
        [TestCase(float.NaN, .006f, PickupEffect.None)]
        public void LowHealthBiasUsesOnlyValidHealthFraction(float health, float roll, PickupEffect expected)
        {
            var schedule = Schedule();
            for (uint i = 1; i <= 4; i++) schedule.Consume(i, 2, 7, 1, 0, 4, () => 1);
            Assert.That(schedule.Consume(5, 2, 7, health, 0, 4, () => roll).Effect, Is.EqualTo(expected));
        }
    }
}
