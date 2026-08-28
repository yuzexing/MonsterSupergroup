using System;
using System.Collections.Generic;

namespace MonsterSupergroup.GAS.Tests
{
    [Serializable]
    internal sealed class TestEquipmentParameters : EquipmentModifierParameters
    {
        public float value;
    }

    [Serializable]
    internal sealed class OtherEquipmentParameters : EquipmentModifierParameters
    {
    }

    [Serializable]
    internal sealed class TestPerkParameters : PerkModifierParameters
    {
        public float value;
    }

    [Serializable]
    internal sealed class OtherPerkParameters : PerkModifierParameters
    {
    }

    internal sealed class TestStaticModifier : StaticStatModifier
    {
        private readonly int priority;

        public TestStaticModifier(uint id, float value = 0f, int priority = 1)
            : base(new EquipmentModifierID(id), new TestEquipmentParameters { value = value })
        {
            this.priority = priority;
        }

        public override int GetSortPriority() => priority;

        public override void Apply(AttackStatsMultipliers multipliers)
        {
            multipliers.damage += ((TestEquipmentParameters)Parameters).value;
        }
    }

    internal sealed class OtherStaticModifier : StaticStatModifier
    {
        public OtherStaticModifier(uint id)
            : base(new EquipmentModifierID(id), new OtherEquipmentParameters())
        {
        }

        public override void Apply(AttackStatsMultipliers multipliers)
        {
        }
    }

    internal sealed class DisposableStaticModifier : StaticStatModifier
    {
        public DisposableStaticModifier(uint id)
            : base(new EquipmentModifierID(id), new TestEquipmentParameters())
        {
        }

        public int DisposeCalls { get; private set; }

        public override void Apply(AttackStatsMultipliers multipliers)
        {
        }

        public override void Dispose()
        {
            DisposeCalls++;
        }
    }

    internal sealed class TestDynamicModifier : DynamicStatModifier
    {
        private readonly int priority;

        public TestDynamicModifier(uint id, int priority = 1)
            : base(new EquipmentModifierID(id), new TestEquipmentParameters())
        {
            this.priority = priority;
        }

        public override int GetSortPriority() => priority;

        public override void Apply(AttackStatsMultipliers multipliers, IWeaponRuntime weapon)
        {
            multipliers.speed += 0.1f;
        }
    }

    internal sealed class TestOnDamageModifier : DynamicOnDamageModifier
    {
        private readonly int priority;

        public TestOnDamageModifier(uint id, int priority = 1)
            : base(new EquipmentModifierID(id), new TestEquipmentParameters())
        {
            this.priority = priority;
        }

        public override int GetSortPriority() => priority;

        public override void Apply(AttackStatsMultipliers multipliers, ICombatTarget target)
        {
            multipliers.damage += 0.25f;
        }
    }

    internal sealed class TestOnHitModifier : OnHitModifier
    {
        private readonly int sortPriority;
        private readonly float rollPriority;
        private readonly float chance;

        public TestOnHitModifier(uint id, float chance = 1f, float rollPriority = 0f, int sortPriority = 1)
            : base(new EquipmentModifierID(id), new TestEquipmentParameters())
        {
            this.chance = chance;
            this.rollPriority = rollPriority;
            this.sortPriority = sortPriority;
        }

        public int Calls { get; private set; }

        public override int GetSortPriority() => sortPriority;
        public override float GetRollChance() => chance;
        public override float GetRollPriority() => rollPriority;

        protected override void ApplyEffect(OnHitModifierArgs args)
        {
            Calls++;
        }
    }

    internal sealed class CallbackOnHitModifier : OnHitModifier
    {
        private readonly Action<OnHitModifierArgs> callback;

        public CallbackOnHitModifier(uint id, Action<OnHitModifierArgs> callback)
            : base(new EquipmentModifierID(id), new TestEquipmentParameters())
        {
            this.callback = callback;
        }

        public override float GetRollChance() => 1f;
        public override float GetRollPriority() => 0f;
        protected override void ApplyEffect(OnHitModifierArgs args) => callback(args);
    }

    internal sealed class TestOnKillModifier : OnKillModifier
    {
        private readonly int sortPriority;
        private readonly float rollPriority;
        private readonly float chance;

        public TestOnKillModifier(uint id, float chance = 1f, float rollPriority = 0f, int sortPriority = 1)
            : base(new EquipmentModifierID(id), new TestEquipmentParameters())
        {
            this.chance = chance;
            this.rollPriority = rollPriority;
            this.sortPriority = sortPriority;
        }

        public int Calls { get; private set; }

        public override int GetSortPriority() => sortPriority;
        public override float GetRollChance() => chance;
        public override float GetRollPriority() => rollPriority;

        protected override void ApplyEffect(OnKillModifierArgs args)
        {
            Calls++;
        }
    }

    internal sealed class UnsupportedModifier : RuntimeEquipmentModifier
    {
        public UnsupportedModifier(uint id)
            : base(new EquipmentModifierID(id), new TestEquipmentParameters())
        {
        }
    }

    internal sealed class TestPerkModifier : WeaponStatsPerkModifier
    {
        private float value;

        public TestPerkModifier(uint id, float value)
            : base(new PerkModifierID(id), new TestPerkParameters { value = value })
        {
            this.value = value;
        }

        public float Value => value;

        public override void Apply(AttackStatsMultipliers multipliers)
        {
            multipliers.speed += value;
        }

        protected override bool StackSameType(RuntimePerkModifier other)
        {
            value += ((TestPerkModifier)other).value;
            return true;
        }
    }

    internal sealed class OtherTestPerkModifier : WeaponStatsPerkModifier
    {
        public OtherTestPerkModifier(uint id)
            : base(new PerkModifierID(id), new OtherPerkParameters())
        {
        }

        public override void Apply(AttackStatsMultipliers multipliers)
        {
        }

        protected override bool StackSameType(RuntimePerkModifier other)
        {
            return true;
        }
    }

    internal sealed class SequenceRandom : IRandomSource
    {
        private readonly Queue<float> values;

        public SequenceRandom(params float[] values)
        {
            this.values = new Queue<float>(values);
        }

        public float Next01()
        {
            return values.Count > 0 ? values.Dequeue() : 0f;
        }
    }

    internal sealed class TestWeapon : IWeaponRuntime
    {
        public TestWeapon(uint combatId, WeaponBehaviourStats stats)
        {
            CombatId = combatId;
            Stats = stats;
        }

        public uint CombatId { get; }
        public WeaponBehaviourStats Stats { get; }
    }

    internal sealed class TestTarget : ICombatTarget, ICombatLifecycleTarget
    {
        private int health;

        public TestTarget(int health)
        {
            this.health = health;
        }

        public bool IsAlive => health > 0;
        public int Health => health;
        public List<DamageInfo> ReceivedDamage { get; } = new List<DamageInfo>();
        public List<StatusApplication> AppliedStatuses { get; } = new List<StatusApplication>();
        public List<PredictedLethalHit> PredictedLethalHits { get; } =
            new List<PredictedLethalHit>();
        public List<ConfirmedKill> ConfirmedKills { get; } = new List<ConfirmedKill>();

        public DamageInfo ReceiveDamage(DamageInfo requestedDamage)
        {
            int accepted = Math.Min(health, requestedDamage.Value);
            health -= accepted;
            var result = new DamageInfo(requestedDamage.Id, accepted, requestedDamage.IsCritical);
            ReceivedDamage.Add(result);
            return result;
        }

        public StatusApplicationResult ApplyStatus(StatusApplication application)
        {
            AppliedStatuses.Add(application);
            return StatusApplicationResult.Added;
        }

        public void RestoreHealth(int value)
        {
            health = value;
        }

        public void ReceivePredictedLethalHit(PredictedLethalHit hit)
        {
            PredictedLethalHits.Add(hit);
        }

        public void ReceiveConfirmedKill(ConfirmedKill kill)
        {
            ConfirmedKills.Add(kill);
        }
    }
}
