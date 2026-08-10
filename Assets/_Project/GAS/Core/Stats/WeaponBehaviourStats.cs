using System;
using System.Collections.Generic;

namespace MonsterSupergroup.GAS
{
    [Serializable]
    public sealed class WeaponBehaviourStats
    {
        public enum StatFormulaMultipliers
        {
            Base = 0,
            BaseAndDynamic = 1,
            BaseAndPlayer = 2,
            BaseAndGlobal = BaseAndPlayer,
            All = 3
        }

        private static readonly AttackStatType[] AllStatTypes =
        {
            AttackStatType.Damage,
            AttackStatType.Size,
            AttackStatType.Speed,
            AttackStatType.Duration,
            AttackStatType.ProjectileCount,
            AttackStatType.CritRate,
            AttackStatType.CritDamage,
            AttackStatType.Knockback
        };

        private AttackStats _baseStats;
        private readonly Dictionary<AttackStatType, AttackStatType> _statsMap;

        public WeaponBehaviourStats(
            AttackStats baseStats,
            AttackStatsMultipliers globalStatsMultipliers = null)
        {
            _baseStats = baseStats;
            BaseStatsMultipliers = new AttackStatsMultipliers();
            DynamicStatsMultipliers = new AttackStatsMultipliers();
            GlobalStatsMultipliers = globalStatsMultipliers?.Clone()
                ?? new AttackStatsMultipliers();
            _statsMap = new Dictionary<AttackStatType, AttackStatType>(AllStatTypes.Length);
            ResetStatRemaps();
        }

        public AttackStats BaseStats => _baseStats;

        public AttackStatsMultipliers BaseStatsMultipliers { get; }

        public AttackStatsMultipliers DynamicStatsMultipliers { get; }

        public AttackStatsMultipliers GlobalStatsMultipliers { get; }

        public int DamageValue => (int)Math.Ceiling(GetStatValue(AttackStatType.Damage));

        public float DamageMultiplierSum => GetRemappedMultiplierSum(AttackStatType.Damage);

        public float SizeValue => GetStatValue(AttackStatType.Size);

        public float SizeMultiplierSum => GetRemappedMultiplierSum(AttackStatType.Size);

        public float SpeedValue => GetStatValue(AttackStatType.Speed);

        public float SpeedMultiplierSum => GetRemappedMultiplierSum(AttackStatType.Speed);

        public float SpeedMultipliersProduct => GetRemappedMultiplierProduct(AttackStatType.Speed);

        public float DurationValue => GetStatValue(AttackStatType.Duration);

        public float DurationMultiplierSum => GetRemappedMultiplierSum(AttackStatType.Duration);

        public int ProjectileCountValue => (int)GetStatValue(AttackStatType.ProjectileCount);

        public float CritRate => GetStatValue(AttackStatType.CritRate);

        public float CritRateMultiplierSum => GetRemappedMultiplierSum(AttackStatType.CritRate);

        public float CritDamageMultiplier => GetStatValue(AttackStatType.CritDamage);

        public float CritDamageMultiplierSum => GetRemappedMultiplierSum(AttackStatType.CritDamage);

        public int CriticalDamageValue => (int)(DamageValue * CritDamageMultiplier);

        public float KnockBackDistance => GetStatValue(AttackStatType.Knockback);

        public float KnockBackMultiplierSum => GetRemappedMultiplierSum(AttackStatType.Knockback);

        public void SetBaseStats(AttackStats baseStats)
        {
            _baseStats = baseStats;
        }

        public void ResetBase()
        {
            BaseStatsMultipliers.Reset();
        }

        public void ResetDynamic()
        {
            DynamicStatsMultipliers.Reset();
        }

        public void ResetGlobal()
        {
            GlobalStatsMultipliers.Reset();
        }

        public void ResetAllMultipliers()
        {
            ResetBase();
            ResetDynamic();
            ResetGlobal();
        }

        public float GetStatValue(
            AttackStatType target,
            StatFormulaMultipliers formulaMultipliers = StatFormulaMultipliers.All,
            AttackStatsMultipliers additionalMultipliers = null)
        {
            AttackStatType mappedType = ResolveMappedStat(target);
            float baseStatValue = GetBaseStatValue(target);

            if (IsAdditiveStat(target))
            {
                float value = baseStatValue
                    + GetMultiplierFromType(mappedType, BaseStatsMultipliers);

                if (IncludesDynamic(formulaMultipliers))
                {
                    value += GetMultiplierFromType(mappedType, DynamicStatsMultipliers);
                }

                if (IncludesGlobal(formulaMultipliers))
                {
                    value += GetMultiplierFromType(target, GlobalStatsMultipliers);
                }

                if (formulaMultipliers == StatFormulaMultipliers.All
                    && additionalMultipliers != null)
                {
                    value += GetMultiplierFromType(mappedType, additionalMultipliers);
                }

                float minimum = target == AttackStatType.ProjectileCount ? 1f : 0f;
                return value < minimum ? minimum : value;
            }

            float result = baseStatValue
                * CalculateMultiplierFormula(GetMultiplierFromType(mappedType, BaseStatsMultipliers));

            if (IncludesDynamic(formulaMultipliers))
            {
                result *= CalculateMultiplierFormula(
                    GetMultiplierFromType(mappedType, DynamicStatsMultipliers));
            }

            if (IncludesGlobal(formulaMultipliers))
            {
                result *= CalculateMultiplierFormula(
                    GetMultiplierFromType(target, GlobalStatsMultipliers));
            }

            if (formulaMultipliers == StatFormulaMultipliers.All
                && additionalMultipliers != null)
            {
                result *= CalculateMultiplierFormula(
                    GetMultiplierFromType(mappedType, additionalMultipliers));
            }

            return result;
        }

        public AttackStatsSnapshot CreateSnapshot(
            AttackStatsMultipliers additionalMultipliers = null)
        {
            float damageBeforeRounding = GetStatValue(
                AttackStatType.Damage,
                StatFormulaMultipliers.All,
                additionalMultipliers);
            int damage = (int)Math.Ceiling(damageBeforeRounding);
            float critDamageMultiplier = GetStatValue(
                AttackStatType.CritDamage,
                StatFormulaMultipliers.All,
                additionalMultipliers);

            return new AttackStatsSnapshot(
                damageBeforeRounding,
                damage,
                (int)(damage * critDamageMultiplier),
                GetStatValue(AttackStatType.CritRate, StatFormulaMultipliers.All, additionalMultipliers),
                critDamageMultiplier,
                GetStatValue(AttackStatType.Speed, StatFormulaMultipliers.All, additionalMultipliers),
                GetStatValue(AttackStatType.Size, StatFormulaMultipliers.All, additionalMultipliers),
                GetStatValue(AttackStatType.Duration, StatFormulaMultipliers.All, additionalMultipliers),
                (int)GetStatValue(
                    AttackStatType.ProjectileCount,
                    StatFormulaMultipliers.All,
                    additionalMultipliers),
                GetStatValue(AttackStatType.Knockback, StatFormulaMultipliers.All, additionalMultipliers),
                _baseStats.damageType);
        }

        public void RemapStat(AttackStatType target, AttackStatType source)
        {
            EnsureSupportedStat(target);
            EnsureSupportedStat(source);

            if (target == source)
            {
                _statsMap[target] = target;
                return;
            }

            AttackStatType current = source;
            while (true)
            {
                if (current == target)
                {
                    throw new InvalidOperationException(
                        $"Remapping {target} to {source} would create a cycle.");
                }

                AttackStatType next = _statsMap[current];
                if (next == current)
                {
                    break;
                }

                current = next;
            }

            _statsMap[target] = source;
        }

        public void ResetStatRemaps()
        {
            for (int i = 0; i < AllStatTypes.Length; i++)
            {
                AttackStatType type = AllStatTypes[i];
                _statsMap[type] = type;
            }
        }

        public float GetMultiplierFromType(
            AttackStatType type,
            AttackStatsMultipliers multipliers)
        {
            if (multipliers == null)
            {
                throw new ArgumentNullException(nameof(multipliers));
            }

            switch (type)
            {
                case AttackStatType.Damage:
                    return multipliers.damage;
                case AttackStatType.Size:
                    return multipliers.size;
                case AttackStatType.Speed:
                    return multipliers.speed;
                case AttackStatType.Duration:
                    return multipliers.duration;
                case AttackStatType.ProjectileCount:
                    return multipliers.projectileCountIncrement;
                case AttackStatType.CritRate:
                    return multipliers.critRate;
                case AttackStatType.CritDamage:
                    return multipliers.critDamage;
                case AttackStatType.Knockback:
                    return multipliers.knockBackMultiplier;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported attack stat.");
            }
        }

        public float CalculateMultiplierFormula(float multiplier)
        {
            if (multiplier >= 0f)
            {
                return 1f + multiplier;
            }

            return 1f / (1f + Math.Abs(multiplier));
        }

        private float GetBaseStatValue(AttackStatType type)
        {
            switch (type)
            {
                case AttackStatType.Damage:
                    return _baseStats.damage;
                case AttackStatType.Size:
                    return _baseStats.size;
                case AttackStatType.Speed:
                    return _baseStats.speed;
                case AttackStatType.Duration:
                    return _baseStats.duration;
                case AttackStatType.ProjectileCount:
                    return _baseStats.projectileCount;
                case AttackStatType.CritRate:
                    return _baseStats.critRate;
                case AttackStatType.CritDamage:
                    return _baseStats.critMultiplier;
                case AttackStatType.Knockback:
                    return _baseStats.knockbackDistance;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported attack stat.");
            }
        }

        private float GetRemappedMultiplierSum(
            AttackStatType target,
            StatFormulaMultipliers formulaMultipliers = StatFormulaMultipliers.All)
        {
            AttackStatType mappedType = ResolveMappedStat(target);
            float value = GetMultiplierFromType(mappedType, BaseStatsMultipliers);

            if (IncludesDynamic(formulaMultipliers))
            {
                value += GetMultiplierFromType(mappedType, DynamicStatsMultipliers);
            }

            if (IncludesGlobal(formulaMultipliers))
            {
                value += GetMultiplierFromType(target, GlobalStatsMultipliers);
            }

            return value;
        }

        private float GetRemappedMultiplierProduct(
            AttackStatType target,
            StatFormulaMultipliers formulaMultipliers = StatFormulaMultipliers.All)
        {
            AttackStatType mappedType = ResolveMappedStat(target);
            float value = CalculateMultiplierFormula(
                GetMultiplierFromType(mappedType, BaseStatsMultipliers));

            if (IncludesDynamic(formulaMultipliers))
            {
                value *= CalculateMultiplierFormula(
                    GetMultiplierFromType(mappedType, DynamicStatsMultipliers));
            }

            if (IncludesGlobal(formulaMultipliers))
            {
                value *= CalculateMultiplierFormula(
                    GetMultiplierFromType(target, GlobalStatsMultipliers));
            }

            return value;
        }

        private AttackStatType ResolveMappedStat(AttackStatType target)
        {
            EnsureSupportedStat(target);

            AttackStatType current = target;
            while (_statsMap[current] != current)
            {
                current = _statsMap[current];
            }

            return current;
        }

        private static bool IsAdditiveStat(AttackStatType type)
        {
            return type == AttackStatType.CritRate
                || type == AttackStatType.CritDamage
                || type == AttackStatType.ProjectileCount;
        }

        private static bool IncludesDynamic(StatFormulaMultipliers formulaMultipliers)
        {
            return formulaMultipliers == StatFormulaMultipliers.BaseAndDynamic
                || formulaMultipliers == StatFormulaMultipliers.All;
        }

        private static bool IncludesGlobal(StatFormulaMultipliers formulaMultipliers)
        {
            return formulaMultipliers == StatFormulaMultipliers.BaseAndPlayer
                || formulaMultipliers == StatFormulaMultipliers.All;
        }

        private static void EnsureSupportedStat(AttackStatType type)
        {
            if (!Enum.IsDefined(typeof(AttackStatType), type))
            {
                throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported attack stat.");
            }
        }
    }
}
