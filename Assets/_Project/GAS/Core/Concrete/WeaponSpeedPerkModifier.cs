using System;

namespace MonsterSupergroup.GAS
{
    [Serializable]
    public sealed class WeaponSpeedPerkModifierParameters : PerkModifierParameters
    {
        public WeaponSpeedPerkModifierParameters()
        {
        }

        public WeaponSpeedPerkModifierParameters(float multiplierIncrement)
        {
            if (float.IsNaN(multiplierIncrement) || float.IsInfinity(multiplierIncrement))
            {
                throw new ArgumentOutOfRangeException(nameof(multiplierIncrement));
            }

            this.multiplierIncrement = multiplierIncrement;
        }

        public float multiplierIncrement;

        public float MultiplierIncrement => multiplierIncrement;
    }

    [PerkModifierType(
        ModifierIdValue,
        "Weapon Speed",
        typeof(WeaponSpeedPerkModifierParameters))]
    public sealed class WeaponSpeedPerkModifier : WeaponStatsPerkModifier
    {
        public const uint ModifierIdValue = 0x05000001u;

        private float multiplierIncrement;

        public WeaponSpeedPerkModifier(WeaponSpeedPerkModifierParameters parameters)
            : base(new PerkModifierID(ModifierIdValue), Validate(parameters))
        {
            multiplierIncrement = parameters.multiplierIncrement;
        }

        public float MultiplierIncrement => multiplierIncrement;

        public override void Apply(AttackStatsMultipliers multipliers)
        {
            if (multipliers == null)
            {
                throw new ArgumentNullException(nameof(multipliers));
            }

            multipliers.speed += multiplierIncrement;
        }

        protected override bool StackSameType(RuntimePerkModifier other)
        {
            if (!(other is WeaponSpeedPerkModifier speedModifier))
            {
                return false;
            }

            multiplierIncrement += speedModifier.multiplierIncrement;
            return true;
        }

        private static WeaponSpeedPerkModifierParameters Validate(WeaponSpeedPerkModifierParameters parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (float.IsNaN(parameters.multiplierIncrement) || float.IsInfinity(parameters.multiplierIncrement))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters),
                    "Weapon speed multiplier increment must be finite.");
            }

            return parameters;
        }
    }
}
