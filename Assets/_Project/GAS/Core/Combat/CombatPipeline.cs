using System;

namespace MonsterSupergroup.GAS
{
    public sealed class CombatPipeline
    {
        private readonly RuntimeEquipmentModifiers modifiers;
        private readonly IRandomSource random;

        public CombatPipeline(RuntimeEquipmentModifiers modifiers, IRandomSource random)
        {
            this.modifiers = modifiers ?? throw new ArgumentNullException(nameof(modifiers));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public AttackSnapshot BeginAttack(
            IWeaponRuntime weapon,
            AttackStatsMultipliers globalMultipliers = null)
        {
            if (weapon == null)
            {
                throw new ArgumentNullException(nameof(weapon));
            }

            WeaponBehaviourStats stats = weapon.Stats ??
                throw new ArgumentException("Weapon runtime must provide stats.", nameof(weapon));

            stats.ResetBase();
            for (int i = 0; i < modifiers.StaticModifiers.Count; i++)
            {
                modifiers.StaticModifiers[i].Apply(stats);
            }

            stats.ResetGlobal();
            if (globalMultipliers != null)
            {
                stats.GlobalStatsMultipliers.CopyFrom(globalMultipliers);
            }

            stats.ResetDynamic();
            for (int i = 0; i < modifiers.DynamicModifiers.Count; i++)
            {
                modifiers.DynamicModifiers[i].Apply(stats, weapon);
            }

            return new AttackSnapshot(weapon, stats.CreateSnapshot());
        }

        public DamageInfo ResolveHit(
            AttackSnapshot attack,
            ICombatTarget target,
            float onHitChanceMultiplier = 1f,
            float onKillChanceMultiplier = 1f,
            float burnDamageMultiplier = 0f)
        {
            if (attack == null)
            {
                throw new ArgumentNullException(nameof(attack));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            ValidateFinite(onHitChanceMultiplier, nameof(onHitChanceMultiplier));
            ValidateFinite(onKillChanceMultiplier, nameof(onKillChanceMultiplier));
            ValidateFinite(burnDamageMultiplier, nameof(burnDamageMultiplier));

            if (!target.IsAlive)
            {
                return new DamageInfo(attack.Weapon.CombatId, 0, false);
            }

            var targetMultipliers = new AttackStatsMultipliers();
            for (int i = 0; i < modifiers.DynamicOnDamageModifiers.Count; i++)
            {
                modifiers.DynamicOnDamageModifiers[i].Apply(targetMultipliers, target);
            }

            int baseDamage = CeilingToNonNegativeInt(
                attack.Stats.DamageBeforeRounding * SignedMultiplier(targetMultipliers.damage));
            float criticalChance = Probability.Clamp01(attack.Stats.CritRate + targetMultipliers.critRate);
            bool isCritical = criticalChance > 0f && random.Next01() < criticalChance;

            int requestedValue = baseDamage;
            if (isCritical)
            {
                float criticalMultiplier = attack.Stats.CritDamageMultiplier + targetMultipliers.critDamage;
                if (criticalMultiplier < 0f)
                {
                    criticalMultiplier = 0f;
                }

                requestedValue = TruncateToNonNegativeInt(baseDamage * criticalMultiplier);
            }

            bool wasAlive = target.IsAlive;
            DamageInfo requestedDamage =
                new DamageInfo(attack.Weapon.CombatId, requestedValue, isCritical);
            DamageInfo appliedDamage = target.ReceiveDamage(requestedDamage);

            if (appliedDamage.Value > 0)
            {
                var onHitArgs = new OnHitModifierArgs(
                    target,
                    attack.Weapon,
                    appliedDamage,
                    random,
                    onHitChanceMultiplier,
                    burnDamageMultiplier);

                for (int i = 0; i < modifiers.OnHitModifiers.Count; i++)
                {
                    modifiers.OnHitModifiers[i].Apply(onHitArgs);
                }
            }

            if (wasAlive && !target.IsAlive)
            {
                var onKillArgs = new OnKillModifierArgs(
                    target,
                    attack.Weapon,
                    appliedDamage,
                    random,
                    onKillChanceMultiplier);

                for (int i = 0; i < modifiers.OnKillModifiers.Count; i++)
                {
                    modifiers.OnKillModifiers[i].Apply(onKillArgs);
                }
            }

            return appliedDamage;
        }

        private static float SignedMultiplier(float value)
        {
            return value >= 0f ? 1f + value : 1f / (1f + Math.Abs(value));
        }

        private static int CeilingToNonNegativeInt(float value)
        {
            if (float.IsNaN(value) || value <= 0f)
            {
                return 0;
            }

            if (float.IsPositiveInfinity(value) || value >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)Math.Ceiling(value);
        }

        private static int TruncateToNonNegativeInt(float value)
        {
            if (float.IsNaN(value) || value <= 0f)
            {
                return 0;
            }

            if (float.IsPositiveInfinity(value) || value >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)value;
        }

        private static void ValidateFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(parameterName, "Multiplier must be finite.");
            }
        }
    }
}
