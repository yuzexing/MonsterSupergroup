using System;

namespace MonsterSupergroup.GAS
{
    public abstract class RuntimeEquipmentModifier : IDisposable
    {
        protected RuntimeEquipmentModifier(
            EquipmentModifierID id,
            EquipmentModifierParameters parameters)
        {
            ID = id;
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        }

        public EquipmentModifierID ID { get; }

        public EquipmentModifierParameters Parameters { get; }

        public virtual int GetSortPriority()
        {
            return 1;
        }

        public virtual void Dispose()
        {
        }
    }

    public abstract class StaticStatModifier : RuntimeEquipmentModifier
    {
        protected StaticStatModifier(
            EquipmentModifierID id,
            EquipmentModifierParameters parameters)
            : base(id, parameters)
        {
        }

        public virtual void Apply(WeaponBehaviourStats stats)
        {
            if (stats == null)
            {
                throw new ArgumentNullException(nameof(stats));
            }

            Apply(stats.BaseStatsMultipliers);
        }

        public abstract void Apply(AttackStatsMultipliers multipliers);
    }

    public abstract class DynamicStatModifier : RuntimeEquipmentModifier
    {
        protected DynamicStatModifier(
            EquipmentModifierID id,
            EquipmentModifierParameters parameters)
            : base(id, parameters)
        {
        }

        public virtual void Apply(WeaponBehaviourStats stats, IWeaponRuntime weapon)
        {
            if (stats == null)
            {
                throw new ArgumentNullException(nameof(stats));
            }

            Apply(stats.DynamicStatsMultipliers, weapon);
        }

        public abstract void Apply(AttackStatsMultipliers multipliers, IWeaponRuntime weapon);
    }

    public abstract class DynamicOnDamageModifier : RuntimeEquipmentModifier
    {
        protected DynamicOnDamageModifier(
            EquipmentModifierID id,
            EquipmentModifierParameters parameters)
            : base(id, parameters)
        {
        }

        public abstract void Apply(AttackStatsMultipliers multipliers, ICombatTarget target);
    }

    public abstract class OnHitModifier : RuntimeEquipmentModifier
    {
        protected OnHitModifier(
            EquipmentModifierID id,
            EquipmentModifierParameters parameters)
            : base(id, parameters)
        {
        }

        public abstract float GetRollChance();

        public abstract float GetRollPriority();

        public virtual BuildTriggerPolicy GetTriggerPolicy() =>
            BuildTriggerPolicy.Default;

        public OnHitModifierArgs Apply(OnHitModifierArgs args)
        {
            float chance = Probability.Clamp01(GetRollChance() * args.OnHitChanceMultiplier);
            if (args.Random.Next01() < chance)
            {
                ApplyEffect(args);
            }

            return args;
        }

        protected abstract void ApplyEffect(OnHitModifierArgs args);
    }

    public abstract class OnPredictedLethalHitModifier : RuntimeEquipmentModifier
    {
        protected OnPredictedLethalHitModifier(
            EquipmentModifierID id,
            EquipmentModifierParameters parameters)
            : base(id, parameters)
        {
        }

        public abstract float GetRollChance();

        public abstract float GetRollPriority();

        public virtual BuildTriggerPolicy GetTriggerPolicy() =>
            BuildTriggerPolicy.Default;

        public OnPredictedLethalHitModifierArgs Apply(OnPredictedLethalHitModifierArgs args)
        {
            float chance = Probability.Clamp01(GetRollChance() * args.ChanceMultiplier);
            if (args.Random.Next01() < chance)
            {
                ApplyEffect(args);
            }

            return args;
        }

        protected abstract void ApplyEffect(OnPredictedLethalHitModifierArgs args);
    }

    /// <summary>
    /// Backward-compatible base class for migrated modifiers. The execution stage is
    /// PredictedLethalHit; the canonical server is solely responsible for ConfirmedKill.
    /// </summary>
    public abstract class OnKillModifier : OnPredictedLethalHitModifier
    {
        protected OnKillModifier(
            EquipmentModifierID id,
            EquipmentModifierParameters parameters)
            : base(id, parameters)
        {
        }

        public OnKillModifierArgs Apply(OnKillModifierArgs args)
        {
            base.Apply(args.AsPredictedLethalHitArgs());
            return args;
        }

        protected sealed override void ApplyEffect(OnPredictedLethalHitModifierArgs args)
        {
            ApplyEffect(new OnKillModifierArgs(args));
        }

        protected abstract void ApplyEffect(OnKillModifierArgs args);
    }

    internal static class Probability
    {
        public static float Clamp01(float value)
        {
            if (float.IsNaN(value) || value <= 0f)
            {
                return 0f;
            }

            return value >= 1f ? 1f : value;
        }
    }
}
