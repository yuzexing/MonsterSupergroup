using System;

namespace MonsterSupergroup.GAS
{
    [Serializable]
    public sealed class DamageStatModifierParameters : EquipmentModifierParameters
    {
        public DamageStatModifierParameters()
        {
        }

        public DamageStatModifierParameters(float multiplierIncrement)
        {
            if (float.IsNaN(multiplierIncrement) || float.IsInfinity(multiplierIncrement))
            {
                throw new ArgumentOutOfRangeException(nameof(multiplierIncrement));
            }

            this.multiplierIncrement = multiplierIncrement;
        }

        public float multiplierIncrement;

        public float MultiplierIncrement => multiplierIncrement;

        public override bool TryGetNumericParameter(int index, out float value)
        {
            value = multiplierIncrement;
            return index == 0;
        }
    }

    [EquipmentModifierType(
        ModifierIdValue,
        "Damage",
        typeof(DamageStatModifierParameters))]
    public sealed class DamageStatModifier : StaticStatModifier
    {
        public const uint ModifierIdValue = 0x01000001u;

        private readonly float multiplierIncrement;

        public DamageStatModifier(DamageStatModifierParameters parameters)
            : base(new EquipmentModifierID(ModifierIdValue), Validate(parameters))
        {
            multiplierIncrement = parameters.multiplierIncrement;
        }

        public override void Apply(AttackStatsMultipliers multipliers)
        {
            if (multipliers == null)
            {
                throw new ArgumentNullException(nameof(multipliers));
            }

            multipliers.damage += multiplierIncrement;
        }

        private static DamageStatModifierParameters Validate(DamageStatModifierParameters parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (float.IsNaN(parameters.multiplierIncrement) || float.IsInfinity(parameters.multiplierIncrement))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters),
                    "Damage multiplier increment must be finite.");
            }

            return parameters;
        }
    }
}
