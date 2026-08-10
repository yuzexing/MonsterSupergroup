using System;
using NUnit.Framework;

namespace MonsterSupergroup.GAS.Tests
{
    public sealed class WeaponBehaviourStatsTests
    {
        [Test]
        public void MultiplicativeStats_ApplyBaseDynamicAndGlobalAsSeparateFactors()
        {
            WeaponBehaviourStats stats = CreateStats(damage: 10, speed: 4f);
            stats.BaseStatsMultipliers.damage = 0.5f;
            stats.DynamicStatsMultipliers.damage = 0.2f;
            stats.GlobalStatsMultipliers.damage = -0.5f;

            Assert.That(stats.GetStatValue(AttackStatType.Damage), Is.EqualTo(12f).Within(0.0001f));
            Assert.That(stats.DamageValue, Is.EqualTo(12));
            Assert.That(stats.CalculateMultiplierFormula(0.25f), Is.EqualTo(1.25f));
            Assert.That(stats.CalculateMultiplierFormula(-1f), Is.EqualTo(0.5f));
        }

        [Test]
        public void AdditiveStats_ClampCritRateAndProjectileCountAtTheirMinimums()
        {
            WeaponBehaviourStats stats = CreateStats(projectileCount: 2, critRate: 0.2f, critMultiplier: 1.5f);
            stats.BaseStatsMultipliers.projectileCountIncrement = -10;
            stats.BaseStatsMultipliers.critRate = -5f;
            stats.BaseStatsMultipliers.critDamage = 0.25f;

            Assert.That(stats.ProjectileCountValue, Is.EqualTo(1));
            Assert.That(stats.CritRate, Is.Zero);
            Assert.That(stats.CritDamageMultiplier, Is.EqualTo(1.75f).Within(0.0001f));
        }

        [Test]
        public void DamageRoundsUpAndCriticalDamageTruncates()
        {
            WeaponBehaviourStats stats = CreateStats(damage: 3, critMultiplier: 1.5f);
            stats.BaseStatsMultipliers.damage = 0.01f;

            Assert.That(stats.DamageValue, Is.EqualTo(4));
            Assert.That(stats.CriticalDamageValue, Is.EqualTo(6));

            stats.SetBaseStats(new AttackStats
            {
                damage = 3,
                critMultiplier = 1.4f,
                projectileCount = 1
            });
            stats.ResetBase();
            Assert.That(stats.CriticalDamageValue, Is.EqualTo(4));
        }

        [Test]
        public void AdditionalMultipliers_AreIncludedOnlyByAllFormula()
        {
            WeaponBehaviourStats stats = CreateStats(damage: 10);
            var additional = new AttackStatsMultipliers { damage = 1f };

            Assert.That(
                stats.GetStatValue(
                    AttackStatType.Damage,
                    WeaponBehaviourStats.StatFormulaMultipliers.Base,
                    additional),
                Is.EqualTo(10f));
            Assert.That(stats.GetStatValue(AttackStatType.Damage, additionalMultipliers: additional), Is.EqualTo(20f));
        }

        [Test]
        public void ResetMethods_ClearOnlyTheirOwnLayer()
        {
            WeaponBehaviourStats stats = CreateStats(damage: 10);
            stats.BaseStatsMultipliers.damage = 1f;
            stats.DynamicStatsMultipliers.damage = 2f;
            stats.GlobalStatsMultipliers.damage = 3f;

            stats.ResetDynamic();
            Assert.That(stats.BaseStatsMultipliers.damage, Is.EqualTo(1f));
            Assert.That(stats.DynamicStatsMultipliers.damage, Is.Zero);
            Assert.That(stats.GlobalStatsMultipliers.damage, Is.EqualTo(3f));

            stats.ResetAllMultipliers();
            Assert.That(stats.BaseStatsMultipliers.damage, Is.Zero);
            Assert.That(stats.GlobalStatsMultipliers.damage, Is.Zero);
        }

        [Test]
        public void MultiplierCopyCloneAndAdd_DoNotMutateTheirInputs()
        {
            var source = new AttackStatsMultipliers { damage = 0.5f, speed = -0.25f, projectileCountIncrement = 2 };
            var destination = new AttackStatsMultipliers { damage = 0.25f };

            destination.AddFrom(source);
            AttackStatsMultipliers clone = source.Clone();
            clone.damage = 10f;

            Assert.That(destination.damage, Is.EqualTo(0.75f));
            Assert.That(destination.speed, Is.EqualTo(-0.25f));
            Assert.That(source.damage, Is.EqualTo(0.5f));
            Assert.That(source.speed, Is.EqualTo(-0.25f));
        }

        [Test]
        public void RemapStat_UsesSourceMultipliersAndRejectsCycles()
        {
            WeaponBehaviourStats stats = CreateStats(damage: 10, speed: 5f, size: 2f);
            stats.BaseStatsMultipliers.speed = 1f;
            stats.RemapStat(AttackStatType.Damage, AttackStatType.Speed);

            Assert.That(stats.DamageValue, Is.EqualTo(20));
            Assert.Throws<InvalidOperationException>(() =>
                stats.RemapStat(AttackStatType.Speed, AttackStatType.Damage));

            stats.ResetStatRemaps();
            Assert.That(stats.DamageValue, Is.EqualTo(10));
        }

        private static WeaponBehaviourStats CreateStats(
            int damage = 1,
            float speed = 1f,
            float size = 1f,
            int projectileCount = 1,
            float critRate = 0f,
            float critMultiplier = 1f)
        {
            return new WeaponBehaviourStats(new AttackStats
            {
                damage = damage,
                speed = speed,
                size = size,
                duration = 1f,
                projectileCount = projectileCount,
                critRate = critRate,
                critMultiplier = critMultiplier,
                knockbackDistance = 1f
            });
        }
    }
}
