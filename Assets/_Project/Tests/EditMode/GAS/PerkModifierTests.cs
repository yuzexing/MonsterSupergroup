using NUnit.Framework;

namespace MonsterSupergroup.GAS.Tests
{
    public sealed class PerkModifierTests
    {
        [Test]
        public void TryStack_RequiresMatchingIdAndParametersType()
        {
            var first = new TestPerkModifier(10, 0.1f);

            Assert.That(first.TryStack(new TestPerkModifier(10, 0.2f)), Is.True);
            Assert.That(first.Value, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(first.TryStack(new TestPerkModifier(11, 0.5f)), Is.False);
            Assert.That(first.TryStack(new OtherTestPerkModifier(10)), Is.False);
            Assert.That(first.Value, Is.EqualTo(0.3f).Within(0.0001f));
        }

        [Test]
        public void WeaponSpeedPerk_StacksAndAppliesToGlobalMultiplier()
        {
            var first = new WeaponSpeedPerkModifier(new WeaponSpeedPerkModifierParameters(0.15f));
            var second = new WeaponSpeedPerkModifier(new WeaponSpeedPerkModifierParameters(0.20f));
            var multipliers = new AttackStatsMultipliers();

            Assert.That(first.TryStack(second), Is.True);
            first.Apply(multipliers);

            Assert.That(first.MultiplierIncrement, Is.EqualTo(0.35f).Within(0.0001f));
            Assert.That(multipliers.speed, Is.EqualTo(0.35f).Within(0.0001f));
        }
    }
}
