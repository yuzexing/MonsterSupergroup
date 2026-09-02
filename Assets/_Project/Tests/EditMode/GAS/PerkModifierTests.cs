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

        [Test]
        public void PureWeaponStatPerks_ApplyToTheExpectedGlobalStatOnly()
        {
            var multipliers = new AttackStatsMultipliers();

            new WeaponDamagePerkModifier(
                new WeaponDamagePerkModifierParameters(0.11f)).Apply(multipliers);
            new WeaponSizePerkModifier(
                new WeaponSizePerkModifierParameters(0.12f)).Apply(multipliers);
            new WeaponDurationPerkModifier(
                new WeaponDurationPerkModifierParameters(0.13f)).Apply(multipliers);
            new WeaponCritRatePerkModifier(
                new WeaponCritRatePerkModifierParameters(0.14f)).Apply(multipliers);
            new WeaponCritMultiplierPerkModifier(
                new WeaponCritMultiplierPerkModifierParameters(0.15f)).Apply(multipliers);
            new WeaponProjectileCountPerkModifier(
                new WeaponProjectileCountPerkModifierParameters(2)).Apply(multipliers);

            Assert.That(multipliers.damage, Is.EqualTo(0.11f).Within(0.0001f));
            Assert.That(multipliers.size, Is.EqualTo(0.12f).Within(0.0001f));
            Assert.That(multipliers.duration, Is.EqualTo(0.13f).Within(0.0001f));
            Assert.That(multipliers.critRate, Is.EqualTo(0.14f).Within(0.0001f));
            Assert.That(multipliers.critDamage, Is.EqualTo(0.15f).Within(0.0001f));
            Assert.That(multipliers.projectileCountIncrement, Is.EqualTo(2));
            Assert.That(multipliers.speed, Is.Zero);
            Assert.That(multipliers.knockBackMultiplier, Is.Zero);
        }

        [Test]
        public void ProjectileCountPerk_StacksAsAnIntegerIncrement()
        {
            var first = new WeaponProjectileCountPerkModifier(
                new WeaponProjectileCountPerkModifierParameters(1));
            var second = new WeaponProjectileCountPerkModifier(
                new WeaponProjectileCountPerkModifierParameters(2));
            var multipliers = new AttackStatsMultipliers();

            Assert.That(first.TryStack(second), Is.True);
            first.Apply(multipliers);

            Assert.That(first.CountIncrement, Is.EqualTo(3));
            Assert.That(multipliers.projectileCountIncrement, Is.EqualTo(3));
        }
    }
}
