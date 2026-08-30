using System;
using NUnit.Framework;

namespace MonsterSupergroup.GAS.Tests
{
    public sealed class DanteNativeGasCoreTests
    {
        [Test]
        public void GeneratedRegistry_CreatesAllEightNativeNumericModifiers()
        {
            var factory = new RuntimeModifierFactory(GeneratedModifierRegistry.Create());

            AssertCreated<DamageStatModifier>(
                factory,
                DamageStatModifier.ModifierIdValue,
                new DamageStatModifierParameters(0.1f));
            AssertCreated<SpeedStatModifier>(
                factory,
                SpeedStatModifier.ModifierIdValue,
                new SpeedStatModifierParameters(0.1f));
            AssertCreated<SizeStatModifier>(
                factory,
                SizeStatModifier.ModifierIdValue,
                new SizeStatModifierParameters(0.1f));
            AssertCreated<DurationStatModifier>(
                factory,
                DurationStatModifier.ModifierIdValue,
                new DurationStatModifierParameters(0.1f));
            AssertCreated<CritRateStatModifier>(
                factory,
                CritRateStatModifier.ModifierIdValue,
                new CritRateStatModifierParameters(0.1f));
            AssertCreated<CritMultiplierStatModifier>(
                factory,
                CritMultiplierStatModifier.ModifierIdValue,
                new CritMultiplierStatModifierParameters(0.1f));
            AssertCreated<ProjectileCountStatModifier>(
                factory,
                ProjectileCountStatModifier.ModifierIdValue,
                new ProjectileCountStatModifierParameters(1));
            AssertCreated<KnockbackStatModifier>(
                factory,
                KnockbackStatModifier.ModifierIdValue,
                new KnockbackStatModifierParameters(0.1f));
        }

        [Test]
        public void NumericModifiers_ReproduceLegacyFormulasAndStackAdditively()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            modifiers.Add(new DamageStatModifier(new DamageStatModifierParameters(0.3f)));
            modifiers.Add(new DamageStatModifier(new DamageStatModifierParameters(0.2f)));
            modifiers.Add(new SpeedStatModifier(new SpeedStatModifierParameters(0.25f)));
            modifiers.Add(new SizeStatModifier(new SizeStatModifierParameters(0.5f)));
            modifiers.Add(new DurationStatModifier(new DurationStatModifierParameters(0.5f)));
            modifiers.Add(new CritRateStatModifier(new CritRateStatModifierParameters(0.2f)));
            modifiers.Add(new CritMultiplierStatModifier(
                new CritMultiplierStatModifierParameters(0.5f)));
            modifiers.Add(new ProjectileCountStatModifier(
                new ProjectileCountStatModifierParameters(2)));
            modifiers.Add(new KnockbackStatModifier(
                new KnockbackStatModifierParameters(1f)));

            var pipeline = new CombatPipeline(modifiers, new SequenceRandom(0.99f));
            using (AttackSnapshot attack = pipeline.BeginAttack(CreateWeapon()))
            {
                Assert.That(attack.Stats.DamageBeforeRounding, Is.EqualTo(15f).Within(0.0001f));
                Assert.That(attack.Stats.Damage, Is.EqualTo(15));
                Assert.That(attack.Stats.Speed, Is.EqualTo(2.5f).Within(0.0001f));
                Assert.That(attack.Stats.Size, Is.EqualTo(4.5f).Within(0.0001f));
                Assert.That(attack.Stats.Duration, Is.EqualTo(6f).Within(0.0001f));
                Assert.That(attack.Stats.CritRate, Is.EqualTo(0.3f).Within(0.0001f));
                Assert.That(
                    attack.Stats.CritDamageMultiplier,
                    Is.EqualTo(2f).Within(0.0001f));
                Assert.That(attack.Stats.ProjectileCount, Is.EqualTo(3));
                Assert.That(attack.Stats.KnockbackDistance, Is.EqualTo(10f).Within(0.0001f));
            }
        }

        [Test]
        public void AttackSnapshot_FreezesStatsAcrossHandleRemovalAndLaterAttacks()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            ModifierHandle handle = modifiers.Add(
                new DamageStatModifier(new DamageStatModifierParameters(0.5f)));
            var pipeline = new CombatPipeline(modifiers, new SequenceRandom(0.99f));
            TestWeapon weapon = CreateWeapon();

            using (AttackSnapshot frozen = pipeline.BeginAttack(weapon))
            {
                Assert.That(frozen.Stats.Damage, Is.EqualTo(15));
                Assert.That(modifiers.Remove(handle), Is.True);

                using (AttackSnapshot later = pipeline.BeginAttack(weapon))
                {
                    Assert.That(later.Stats.Damage, Is.EqualTo(10));
                }

                Assert.That(frozen.Stats.Damage, Is.EqualTo(15));
                DamageInfo damage = pipeline.ResolveHit(frozen, new TestTarget(100));
                Assert.That(damage.Value, Is.EqualTo(15));
            }
        }

        [Test]
        public void AttackSnapshot_RetainsHitStageModifierUntilFinalLeaseEnds()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            var onHit = new DisposableOnHitModifier();
            ModifierHandle handle = modifiers.Add(onHit);
            var pipeline = new CombatPipeline(modifiers, new SequenceRandom(0f));
            AttackSnapshot owner = pipeline.BeginAttack(CreateWeapon());
            AttackSnapshotLease projectileLease = owner.Retain();

            owner.Dispose();
            Assert.That(modifiers.Remove(handle), Is.True);
            Assert.That(onHit.DisposeCalls, Is.Zero);

            pipeline.ResolveHit(projectileLease.Snapshot, new TestTarget(100));
            Assert.That(onHit.ApplyCalls, Is.EqualTo(1));
            Assert.That(onHit.DisposeCalls, Is.Zero);

            projectileLease.Dispose();
            Assert.That(onHit.DisposeCalls, Is.EqualTo(1));
        }

        private static void AssertCreated<T>(
            RuntimeModifierFactory factory,
            uint id,
            EquipmentModifierParameters parameters)
            where T : RuntimeEquipmentModifier
        {
            RuntimeEquipmentModifier modifier = factory.CreateEquipment(
                new EquipmentModifierID(id),
                parameters);
            Assert.That(modifier, Is.TypeOf<T>());
        }

        private static TestWeapon CreateWeapon()
        {
            return new TestWeapon(2, new WeaponBehaviourStats(new AttackStats
            {
                damage = 10,
                speed = 2f,
                size = 3f,
                duration = 4f,
                projectileCount = 1,
                critRate = 0.1f,
                critMultiplier = 1.5f,
                knockbackDistance = 5f
            }));
        }

        private sealed class DisposableOnHitModifier : OnHitModifier
        {
            public DisposableOnHitModifier()
                : base(new EquipmentModifierID(0x7F000001u), new TestEquipmentParameters())
            {
            }

            public int ApplyCalls { get; private set; }
            public int DisposeCalls { get; private set; }

            public override float GetRollChance() => 1f;
            public override float GetRollPriority() => 0f;

            protected override void ApplyEffect(OnHitModifierArgs args)
            {
                ApplyCalls++;
            }

            public override void Dispose()
            {
                DisposeCalls++;
            }
        }
    }
}
