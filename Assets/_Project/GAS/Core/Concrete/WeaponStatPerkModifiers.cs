using System;

namespace MonsterSupergroup.GAS
{
    [Serializable]
    public abstract class FloatWeaponStatPerkModifierParameters : PerkModifierParameters
    {
        public float multiplierIncrement;

        protected FloatWeaponStatPerkModifierParameters()
        {
        }

        protected FloatWeaponStatPerkModifierParameters(float multiplierIncrement)
        {
            if (!IsFinite(multiplierIncrement))
            {
                throw new ArgumentOutOfRangeException(nameof(multiplierIncrement));
            }

            this.multiplierIncrement = multiplierIncrement;
        }

        public float MultiplierIncrement => multiplierIncrement;

        public override bool TryGetNumericParameter(int index, out float value)
        {
            value = multiplierIncrement;
            return index == 0;
        }

        internal static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public abstract class AdditiveWeaponStatPerkModifier<TParameters> :
        WeaponStatsPerkModifier
        where TParameters : FloatWeaponStatPerkModifierParameters
    {
        private float multiplierIncrement;

        protected AdditiveWeaponStatPerkModifier(
            PerkModifierID id,
            TParameters parameters)
            : base(id, Validate(parameters))
        {
            multiplierIncrement = parameters.MultiplierIncrement;
        }

        public float MultiplierIncrement => multiplierIncrement;

        protected override bool StackSameType(RuntimePerkModifier other)
        {
            if (!(other is AdditiveWeaponStatPerkModifier<TParameters> modifier))
            {
                return false;
            }

            multiplierIncrement += modifier.multiplierIncrement;
            return true;
        }

        protected static TParameters Validate(TParameters parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (!FloatWeaponStatPerkModifierParameters.IsFinite(
                parameters.MultiplierIncrement))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters),
                    "Weapon stat perk increment must be finite.");
            }

            return parameters;
        }
    }

    [Serializable]
    public sealed class WeaponDamagePerkModifierParameters :
        FloatWeaponStatPerkModifierParameters
    {
        public WeaponDamagePerkModifierParameters()
        {
        }

        public WeaponDamagePerkModifierParameters(float multiplierIncrement)
            : base(multiplierIncrement)
        {
        }
    }

    [PerkModifierType(
        ModifierIdValue,
        "Weapon Damage",
        typeof(WeaponDamagePerkModifierParameters))]
    public sealed class WeaponDamagePerkModifier :
        AdditiveWeaponStatPerkModifier<WeaponDamagePerkModifierParameters>
    {
        public const uint ModifierIdValue = 0x05000002u;

        public WeaponDamagePerkModifier(WeaponDamagePerkModifierParameters parameters)
            : base(new PerkModifierID(ModifierIdValue), parameters)
        {
        }

        public override void Apply(AttackStatsMultipliers multipliers)
        {
            if (multipliers == null)
            {
                throw new ArgumentNullException(nameof(multipliers));
            }

            multipliers.damage += MultiplierIncrement;
        }
    }

    [Serializable]
    public sealed class WeaponSizePerkModifierParameters :
        FloatWeaponStatPerkModifierParameters
    {
        public WeaponSizePerkModifierParameters()
        {
        }

        public WeaponSizePerkModifierParameters(float multiplierIncrement)
            : base(multiplierIncrement)
        {
        }
    }

    [PerkModifierType(
        ModifierIdValue,
        "Weapon Size",
        typeof(WeaponSizePerkModifierParameters))]
    public sealed class WeaponSizePerkModifier :
        AdditiveWeaponStatPerkModifier<WeaponSizePerkModifierParameters>
    {
        public const uint ModifierIdValue = 0x05000003u;

        public WeaponSizePerkModifier(WeaponSizePerkModifierParameters parameters)
            : base(new PerkModifierID(ModifierIdValue), parameters)
        {
        }

        public override void Apply(AttackStatsMultipliers multipliers)
        {
            if (multipliers == null)
            {
                throw new ArgumentNullException(nameof(multipliers));
            }

            multipliers.size += MultiplierIncrement;
        }
    }

    [Serializable]
    public sealed class WeaponDurationPerkModifierParameters :
        FloatWeaponStatPerkModifierParameters
    {
        public WeaponDurationPerkModifierParameters()
        {
        }

        public WeaponDurationPerkModifierParameters(float multiplierIncrement)
            : base(multiplierIncrement)
        {
        }
    }

    [PerkModifierType(
        ModifierIdValue,
        "Weapon Duration",
        typeof(WeaponDurationPerkModifierParameters))]
    public sealed class WeaponDurationPerkModifier :
        AdditiveWeaponStatPerkModifier<WeaponDurationPerkModifierParameters>
    {
        public const uint ModifierIdValue = 0x05000004u;

        public WeaponDurationPerkModifier(WeaponDurationPerkModifierParameters parameters)
            : base(new PerkModifierID(ModifierIdValue), parameters)
        {
        }

        public override void Apply(AttackStatsMultipliers multipliers)
        {
            if (multipliers == null)
            {
                throw new ArgumentNullException(nameof(multipliers));
            }

            multipliers.duration += MultiplierIncrement;
        }
    }

    [Serializable]
    public sealed class WeaponCritRatePerkModifierParameters :
        FloatWeaponStatPerkModifierParameters
    {
        public WeaponCritRatePerkModifierParameters()
        {
        }

        public WeaponCritRatePerkModifierParameters(float multiplierIncrement)
            : base(multiplierIncrement)
        {
        }
    }

    [PerkModifierType(
        ModifierIdValue,
        "Weapon Crit Rate",
        typeof(WeaponCritRatePerkModifierParameters))]
    public sealed class WeaponCritRatePerkModifier :
        AdditiveWeaponStatPerkModifier<WeaponCritRatePerkModifierParameters>
    {
        public const uint ModifierIdValue = 0x05000005u;

        public WeaponCritRatePerkModifier(WeaponCritRatePerkModifierParameters parameters)
            : base(new PerkModifierID(ModifierIdValue), parameters)
        {
        }

        public override void Apply(AttackStatsMultipliers multipliers)
        {
            if (multipliers == null)
            {
                throw new ArgumentNullException(nameof(multipliers));
            }

            multipliers.critRate += MultiplierIncrement;
        }
    }

    [Serializable]
    public sealed class WeaponCritMultiplierPerkModifierParameters :
        FloatWeaponStatPerkModifierParameters
    {
        public WeaponCritMultiplierPerkModifierParameters()
        {
        }

        public WeaponCritMultiplierPerkModifierParameters(float multiplierIncrement)
            : base(multiplierIncrement)
        {
        }
    }

    [PerkModifierType(
        ModifierIdValue,
        "Weapon Crit Damage",
        typeof(WeaponCritMultiplierPerkModifierParameters))]
    public sealed class WeaponCritMultiplierPerkModifier :
        AdditiveWeaponStatPerkModifier<WeaponCritMultiplierPerkModifierParameters>
    {
        public const uint ModifierIdValue = 0x05000006u;

        public WeaponCritMultiplierPerkModifier(
            WeaponCritMultiplierPerkModifierParameters parameters)
            : base(new PerkModifierID(ModifierIdValue), parameters)
        {
        }

        public override void Apply(AttackStatsMultipliers multipliers)
        {
            if (multipliers == null)
            {
                throw new ArgumentNullException(nameof(multipliers));
            }

            multipliers.critDamage += MultiplierIncrement;
        }
    }

    [Serializable]
    public sealed class WeaponProjectileCountPerkModifierParameters :
        PerkModifierParameters
    {
        public int countIncrement;

        public WeaponProjectileCountPerkModifierParameters()
        {
        }

        public WeaponProjectileCountPerkModifierParameters(int countIncrement)
        {
            this.countIncrement = countIncrement;
        }

        public int CountIncrement => countIncrement;

        public override bool TryGetNumericParameter(int index, out float value)
        {
            value = countIncrement;
            return index == 0;
        }
    }

    [PerkModifierType(
        ModifierIdValue,
        "Projectile Increment",
        typeof(WeaponProjectileCountPerkModifierParameters))]
    public sealed class WeaponProjectileCountPerkModifier : WeaponStatsPerkModifier
    {
        public const uint ModifierIdValue = 0x05000007u;

        private int countIncrement;

        public WeaponProjectileCountPerkModifier(
            WeaponProjectileCountPerkModifierParameters parameters)
            : base(new PerkModifierID(ModifierIdValue), Validate(parameters))
        {
            countIncrement = parameters.CountIncrement;
        }

        public int CountIncrement => countIncrement;

        public override void Apply(AttackStatsMultipliers multipliers)
        {
            if (multipliers == null)
            {
                throw new ArgumentNullException(nameof(multipliers));
            }

            multipliers.projectileCountIncrement += countIncrement;
        }

        protected override bool StackSameType(RuntimePerkModifier other)
        {
            if (!(other is WeaponProjectileCountPerkModifier modifier))
            {
                return false;
            }

            countIncrement += modifier.countIncrement;
            return true;
        }

        private static WeaponProjectileCountPerkModifierParameters Validate(
            WeaponProjectileCountPerkModifierParameters parameters)
        {
            return parameters ?? throw new ArgumentNullException(nameof(parameters));
        }
    }
}
