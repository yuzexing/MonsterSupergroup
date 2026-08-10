using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MonsterSupergroup.GAS.Tests
{
    public sealed class GasCoreSmokeTests
    {
        [Test]
        public void Core_ConstructsWithoutBootstrapSceneOrUnityServices()
        {
            ModifierRegistry registry = GeneratedModifierRegistry.Create();
            var factory = new RuntimeModifierFactory(registry);
            var modifiers = new RuntimeEquipmentModifiers();
            var statuses = new StatusController(_ => { });
            var stats = new WeaponBehaviourStats(BaseStats());

            Assert.That(factory, Is.Not.Null);
            Assert.That(modifiers.Count, Is.Zero);
            Assert.That(statuses.Count, Is.Zero);
            Assert.That(stats.DamageValue, Is.EqualTo(10));
        }

        [Test]
        public void FullSlice_DamageBeginAttackBurnTicksAndSpeedPerkRunInPureCSharp()
        {
            var factory = new RuntimeModifierFactory(GeneratedModifierRegistry.Create());
            var modifiers = new RuntimeEquipmentModifiers();
            modifiers.Add(factory.CreateEquipment(
                new EquipmentModifierID(DamageStatModifier.ModifierIdValue),
                new DamageStatModifierParameters(0.5f)));
            modifiers.Add(factory.CreateEquipment(
                new EquipmentModifierID(OnHitBurnModifier.ModifierIdValue),
                new OnHitBurnModifierParameters(1f, 0.5f, 2, 0.25f)));

            var global = new AttackStatsMultipliers();
            var speedPerk = (WeaponSpeedPerkModifier)factory.CreatePerk(
                new PerkModifierID(WeaponSpeedPerkModifier.ModifierIdValue),
                new WeaponSpeedPerkModifierParameters(0.25f));
            speedPerk.Apply(global);

            var weapon = new SmokeWeapon(99, new WeaponBehaviourStats(BaseStats()));
            var target = new SmokeTarget(100);
            var pipeline = new CombatPipeline(modifiers, new FixedRandom(0f));

            AttackSnapshot attack = pipeline.BeginAttack(weapon, global);
            DamageInfo directDamage = pipeline.ResolveHit(attack, target);
            target.Statuses.Advance(1f);

            Assert.That(attack.Stats.Damage, Is.EqualTo(15));
            Assert.That(attack.Stats.Speed, Is.EqualTo(2.5f).Within(0.0001f));
            Assert.That(directDamage.Value, Is.EqualTo(15));
            Assert.That(target.StatusTickDamage, Is.EqualTo(14));
            Assert.That(target.Health, Is.EqualTo(71));
            Assert.That(target.Statuses.Count, Is.Zero);
        }

        [Test]
        public void StatusTiming_ManySmallDeltasMatchesOneLargeDelta()
        {
            List<StatusTick> smallTicks = AdvanceStatus(10, 0.1f);
            List<StatusTick> largeTicks = AdvanceStatus(1, 1f);

            Assert.That(smallTicks.Count, Is.EqualTo(4));
            Assert.That(largeTicks.Count, Is.EqualTo(4));
            for (int index = 0; index < smallTicks.Count; index++)
            {
                Assert.That(smallTicks[index].Damage, Is.EqualTo(largeTicks[index].Damage));
                Assert.That(smallTicks[index].TickIndex, Is.EqualTo(largeTicks[index].TickIndex));
                Assert.That(smallTicks[index].IsFinalTick, Is.EqualTo(largeTicks[index].IsFinalTick));
            }
        }

        [Test]
        public void OneHundredRuntimeContainers_DoNotLeakStaticState()
        {
            var factory = new RuntimeModifierFactory(GeneratedModifierRegistry.Create());
            for (int index = 0; index < 100; index++)
            {
                var modifiers = new RuntimeEquipmentModifiers();
                modifiers.Add(factory.CreateEquipment(
                    new EquipmentModifierID(DamageStatModifier.ModifierIdValue),
                    new DamageStatModifierParameters(0.1f)));
                var pipeline = new CombatPipeline(modifiers, new FixedRandom(0f));
                var weapon = new SmokeWeapon((uint)(index + 1), new WeaponBehaviourStats(BaseStats()));

                Assert.That(pipeline.BeginAttack(weapon).Stats.Damage, Is.EqualTo(11));
                modifiers.Clear();
                Assert.That(modifiers.Count, Is.Zero);
            }

            Assert.That(new RuntimeEquipmentModifiers().Count, Is.Zero);
        }

        private static List<StatusTick> AdvanceStatus(int steps, float delta)
        {
            var ticks = new List<StatusTick>();
            var controller = new StatusController(ticks.Add);
            controller.Apply(new StatusApplication(
                OnHitBurnModifier.BurnDefinition,
                3,
                4,
                0.25f,
                12f,
                7));
            for (int index = 0; index < steps; index++)
            {
                controller.Advance(delta);
            }

            return ticks;
        }

        private static AttackStats BaseStats()
        {
            return new AttackStats
            {
                damage = 10,
                critMultiplier = 1.5f,
                critRate = 0f,
                speed = 2f,
                size = 1f,
                duration = 1f,
                projectileCount = 1,
                knockbackDistance = 1f,
                damageType = DamageType.Normal
            };
        }

        private sealed class FixedRandom : IRandomSource
        {
            private readonly float value;

            public FixedRandom(float value)
            {
                this.value = value;
            }

            public float Next01() => value;
        }

        private sealed class SmokeWeapon : IWeaponRuntime
        {
            public SmokeWeapon(uint combatId, WeaponBehaviourStats stats)
            {
                CombatId = combatId;
                Stats = stats;
            }

            public uint CombatId { get; }
            public WeaponBehaviourStats Stats { get; }
        }

        private sealed class SmokeTarget : ICombatTarget
        {
            public SmokeTarget(int health)
            {
                Health = health;
                Statuses = new StatusController(OnStatusTick);
            }

            public bool IsAlive => Health > 0;
            public int Health { get; private set; }
            public int StatusTickDamage { get; private set; }
            public StatusController Statuses { get; }

            public DamageInfo ReceiveDamage(DamageInfo requestedDamage)
            {
                int accepted = Math.Min(Health, requestedDamage.Value);
                Health -= accepted;
                return new DamageInfo(requestedDamage.Id, accepted, requestedDamage.IsCritical);
            }

            public StatusApplicationResult ApplyStatus(StatusApplication application)
            {
                return Statuses.Apply(application);
            }

            private void OnStatusTick(StatusTick tick)
            {
                DamageInfo applied = ReceiveDamage(tick.Damage);
                StatusTickDamage += applied.Value;
            }
        }
    }
}
