using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MonsterSupergroup.GAS.Tests
{
    public sealed class CombatPipelineTests
    {
        [Test]
        public void BeginAttack_RebuildsStaticGlobalAndDynamicLayersWithoutAccumulation()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            modifiers.Add(new DamageStatModifier(new DamageStatModifierParameters(0.5f)));
            modifiers.Add(new TestDynamicModifier(10));
            var global = new AttackStatsMultipliers { damage = 0.2f };
            TestWeapon weapon = Weapon(damage: 10, speed: 5f);
            var pipeline = new CombatPipeline(modifiers, new SequenceRandom());

            AttackSnapshot first = pipeline.BeginAttack(weapon, global);
            AttackSnapshot second = pipeline.BeginAttack(weapon, global);

            Assert.That(first.Stats.Damage, Is.EqualTo(18));
            Assert.That(first.Stats.Speed, Is.EqualTo(5.5f).Within(0.0001f));
            Assert.That(second.Stats.Damage, Is.EqualTo(18));
            Assert.That(second.Stats.Speed, Is.EqualTo(5.5f).Within(0.0001f));
        }

        [Test]
        public void RemovingStaticModifier_RecomputesFromBaseInsteadOfInvertingPriorResult()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            ModifierHandle handle = modifiers.Add(
                new DamageStatModifier(new DamageStatModifierParameters(0.5f)));
            TestWeapon weapon = Weapon(damage: 10);
            var pipeline = new CombatPipeline(modifiers, new SequenceRandom());

            Assert.That(pipeline.BeginAttack(weapon).Stats.Damage, Is.EqualTo(15));
            Assert.That(modifiers.Remove(handle), Is.True);
            Assert.That(pipeline.BeginAttack(weapon).Stats.Damage, Is.EqualTo(10));
        }

        [Test]
        public void ResolveHit_AppliesOnDamageToIndependentPerTargetAccumulator()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            modifiers.Add(new TestOnDamageModifier(1));
            TestWeapon weapon = Weapon(damage: 10);
            var pipeline = new CombatPipeline(modifiers, new SequenceRandom());
            AttackSnapshot attack = pipeline.BeginAttack(weapon);
            var first = new TestTarget(100);
            var second = new TestTarget(100);

            DamageInfo firstDamage = pipeline.ResolveHit(attack, first);
            DamageInfo secondDamage = pipeline.ResolveHit(attack, second);

            Assert.That(firstDamage.Value, Is.EqualTo(13));
            Assert.That(secondDamage.Value, Is.EqualTo(13));
            Assert.That(first.Health, Is.EqualTo(87));
            Assert.That(second.Health, Is.EqualTo(87));
        }

        [Test]
        public void ResolveHit_RoundsDamageOnlyAfterPerTargetMultipliersAreApplied()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            modifiers.Add(new DamageStatModifier(new DamageStatModifierParameters(0.1f)));
            modifiers.Add(new TestOnDamageModifier(2));
            var pipeline = new CombatPipeline(modifiers, new SequenceRandom());
            AttackSnapshot attack = pipeline.BeginAttack(Weapon(damage: 1));

            DamageInfo damage = pipeline.ResolveHit(attack, new TestTarget(100));

            Assert.That(attack.Stats.DamageBeforeRounding, Is.EqualTo(1.1f).Within(0.0001f));
            Assert.That(attack.Stats.Damage, Is.EqualTo(2));
            Assert.That(damage.Value, Is.EqualTo(2));
        }

        [Test]
        public void ResolveHit_ChanceZeroNeverTriggersAndChanceOneAlwaysTriggers()
        {
            var never = new TestOnHitModifier(1, chance: 0f);
            var always = new TestOnHitModifier(2, chance: 1f);
            var modifiers = new RuntimeEquipmentModifiers();
            modifiers.Add(never);
            modifiers.Add(always);
            var pipeline = new CombatPipeline(modifiers, new SequenceRandom(0f, 0.999f));
            AttackSnapshot attack = pipeline.BeginAttack(Weapon(damage: 1));

            pipeline.ResolveHit(attack, new TestTarget(100));

            Assert.That(never.Calls, Is.Zero);
            Assert.That(always.Calls, Is.EqualTo(1));
        }

        [Test]
        public void ResolveHit_ClampsGlobalChanceMultiplierBeforeRolling()
        {
            var onHit = new TestOnHitModifier(1, chance: 0.75f);
            var modifiers = new RuntimeEquipmentModifiers();
            modifiers.Add(onHit);
            var pipeline = new CombatPipeline(modifiers, new SequenceRandom(0.999f, 0f));
            AttackSnapshot attack = pipeline.BeginAttack(Weapon(damage: 1));

            pipeline.ResolveHit(attack, new TestTarget(100), onHitChanceMultiplier: 2f);
            pipeline.ResolveHit(attack, new TestTarget(100), onHitChanceMultiplier: -1f);

            Assert.That(onHit.Calls, Is.EqualTo(1));
        }

        [Test]
        public void ResolveHit_InvokesOnKillOnlyForTheAliveToDeadTransition()
        {
            var onKill = new TestOnKillModifier(1);
            var modifiers = new RuntimeEquipmentModifiers();
            modifiers.Add(onKill);
            var pipeline = new CombatPipeline(modifiers, new SequenceRandom(0f));
            AttackSnapshot attack = pipeline.BeginAttack(Weapon(damage: 10));
            var target = new TestTarget(5);

            pipeline.ResolveHit(attack, target);
            pipeline.ResolveHit(attack, target);

            Assert.That(onKill.Calls, Is.EqualTo(1));
            Assert.That(target.ReceivedDamage, Has.Count.EqualTo(1));
        }

        [Test]
        public void ResolveHit_OnHitEffectDeathTriggersOnKill()
        {
            var onKill = new TestOnKillModifier(2);
            var modifiers = new RuntimeEquipmentModifiers();
            modifiers.Add(new CallbackOnHitModifier(
                1,
                args => args.Target.ReceiveDamage(new DamageInfo(args.DamageInfo.Id, 100, false))));
            modifiers.Add(onKill);
            var pipeline = new CombatPipeline(modifiers, new SequenceRandom(0f, 0f));
            AttackSnapshot attack = pipeline.BeginAttack(Weapon(damage: 1));

            pipeline.ResolveHit(attack, new TestTarget(10));

            Assert.That(onKill.Calls, Is.EqualTo(1));
        }

        [Test]
        public void ResolveHit_OnHitRevivalSuppressesOnKill()
        {
            var onKill = new TestOnKillModifier(2);
            var modifiers = new RuntimeEquipmentModifiers();
            modifiers.Add(new CallbackOnHitModifier(
                1,
                args => ((TestTarget)args.Target).RestoreHealth(5)));
            modifiers.Add(onKill);
            var pipeline = new CombatPipeline(modifiers, new SequenceRandom(0f));
            AttackSnapshot attack = pipeline.BeginAttack(Weapon(damage: 1));

            pipeline.ResolveHit(attack, new TestTarget(1));

            Assert.That(onKill.Calls, Is.Zero);
        }

        [Test]
        public void ResolveHit_CriticalDamageUsesTruncation()
        {
            var pipeline = new CombatPipeline(new RuntimeEquipmentModifiers(), new SequenceRandom(0f));
            AttackSnapshot attack = pipeline.BeginAttack(Weapon(damage: 3, critRate: 1f, critMultiplier: 1.5f));

            DamageInfo damage = pipeline.ResolveHit(attack, new TestTarget(100));

            Assert.That(damage.IsCritical, Is.True);
            Assert.That(damage.Value, Is.EqualTo(4));
        }

        [Test]
        public void OnHitBurn_CreatesExpectedHighestPriorityStatusOnLivingTarget()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            modifiers.Add(new OnHitBurnModifier(new OnHitBurnModifierParameters(1f, 0.5f, 3, 0.25f)));
            var pipeline = new CombatPipeline(modifiers, new SequenceRandom(0f));
            AttackSnapshot attack = pipeline.BeginAttack(Weapon(damage: 10));
            var target = new TestTarget(100);

            pipeline.ResolveHit(attack, target, burnDamageMultiplier: 0.5f);

            Assert.That(target.AppliedStatuses, Has.Count.EqualTo(1));
            StatusApplication burn = target.AppliedStatuses[0];
            Assert.That(burn.Definition.Id, Is.EqualTo(EnemyStatusID.Burn));
            Assert.That(burn.Definition.StackMode, Is.EqualTo(StatusStackMode.HighestPriority));
            Assert.That(burn.TickDamage, Is.EqualTo(7));
            Assert.That(burn.NumberOfHits, Is.EqualTo(3));
            Assert.That(burn.Priority, Is.EqualTo(21f));
        }

        [Test]
        public void OnHitBurn_UsesSignedFormulaForNegativeGlobalMultiplier()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            modifiers.Add(new OnHitBurnModifier(new OnHitBurnModifierParameters(1f, 0.5f, 2, 0.25f)));
            var pipeline = new CombatPipeline(modifiers, new SequenceRandom(0f));
            AttackSnapshot attack = pipeline.BeginAttack(Weapon(damage: 10));
            var target = new TestTarget(100);

            pipeline.ResolveHit(attack, target, burnDamageMultiplier: -0.5f);

            StatusApplication burn = target.AppliedStatuses[0];
            Assert.That(burn.TickDamage, Is.EqualTo(3));
            Assert.That(burn.Priority, Is.EqualTo(6f));
        }

        [Test]
        public void StatusTicks_DoNotReenterAttackOnHitOrOnKillPipeline()
        {
            var onHit = new TestOnHitModifier(1);
            var onKill = new TestOnKillModifier(2);
            var modifiers = new RuntimeEquipmentModifiers();
            modifiers.Add(onHit);
            modifiers.Add(onKill);
            var target = new TestTarget(100);
            var pipeline = new CombatPipeline(modifiers, new SequenceRandom(0f));
            AttackSnapshot attack = pipeline.BeginAttack(Weapon(damage: 1));
            pipeline.ResolveHit(attack, target);
            Assert.That(onHit.Calls, Is.EqualTo(1));

            var statuses = new StatusController(tick => target.ReceiveDamage(tick.Damage));
            statuses.Apply(new StatusApplication(
                OnHitBurnModifier.BurnDefinition,
                2,
                2,
                0.1f,
                4f));
            statuses.Advance(1f);

            Assert.That(onHit.Calls, Is.EqualTo(1));
            Assert.That(onKill.Calls, Is.Zero);
            Assert.That(target.ReceivedDamage, Has.Count.EqualTo(3));
        }

        [Test]
        public void Pipeline_ExecutesStagesInDeclaredOrder()
        {
            var calls = new List<string>();
            var modifiers = new RuntimeEquipmentModifiers();
            modifiers.Add(new LoggingStaticModifier(1, calls));
            modifiers.Add(new LoggingDynamicModifier(2, calls));
            modifiers.Add(new LoggingOnDamageModifier(3, calls));
            modifiers.Add(new LoggingOnHitModifier(4, calls));
            var pipeline = new CombatPipeline(modifiers, new SequenceRandom(0f));

            AttackSnapshot attack = pipeline.BeginAttack(Weapon(damage: 1));
            pipeline.ResolveHit(attack, new TestTarget(100));

            Assert.That(calls, Is.EqualTo(new[] { "Static", "Dynamic", "DynamicOnDamage", "OnHit" }));
        }

        private static TestWeapon Weapon(
            int damage,
            float speed = 1f,
            float critRate = 0f,
            float critMultiplier = 1f)
        {
            return new TestWeapon(77, new WeaponBehaviourStats(new AttackStats
            {
                damage = damage,
                speed = speed,
                size = 1f,
                duration = 1f,
                projectileCount = 1,
                critRate = critRate,
                critMultiplier = critMultiplier,
                knockbackDistance = 1f
            }));
        }

        private sealed class LoggingStaticModifier : StaticStatModifier
        {
            private readonly ICollection<string> calls;

            public LoggingStaticModifier(uint id, ICollection<string> calls)
                : base(new EquipmentModifierID(id), new TestEquipmentParameters())
            {
                this.calls = calls;
            }

            public override void Apply(AttackStatsMultipliers multipliers) => calls.Add("Static");
        }

        private sealed class LoggingDynamicModifier : DynamicStatModifier
        {
            private readonly ICollection<string> calls;

            public LoggingDynamicModifier(uint id, ICollection<string> calls)
                : base(new EquipmentModifierID(id), new TestEquipmentParameters())
            {
                this.calls = calls;
            }

            public override void Apply(AttackStatsMultipliers multipliers, IWeaponRuntime weapon) =>
                calls.Add("Dynamic");
        }

        private sealed class LoggingOnDamageModifier : DynamicOnDamageModifier
        {
            private readonly ICollection<string> calls;

            public LoggingOnDamageModifier(uint id, ICollection<string> calls)
                : base(new EquipmentModifierID(id), new TestEquipmentParameters())
            {
                this.calls = calls;
            }

            public override void Apply(AttackStatsMultipliers multipliers, ICombatTarget target) =>
                calls.Add("DynamicOnDamage");
        }

        private sealed class LoggingOnHitModifier : OnHitModifier
        {
            private readonly ICollection<string> calls;

            public LoggingOnHitModifier(uint id, ICollection<string> calls)
                : base(new EquipmentModifierID(id), new TestEquipmentParameters())
            {
                this.calls = calls;
            }

            public override float GetRollChance() => 1f;
            public override float GetRollPriority() => 0f;
            protected override void ApplyEffect(OnHitModifierArgs args) => calls.Add("OnHit");
        }
    }
}
