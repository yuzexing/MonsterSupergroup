using System;

namespace MonsterSupergroup.GAS
{
    [Serializable]
    public sealed class CritMultiplierStatModifierParameters : EquipmentModifierParameters
    {
        public CritMultiplierStatModifierParameters()
        {
        }

        public CritMultiplierStatModifierParameters(float multiplierIncrement)
        {
            this.multiplierIncrement = NumericModifierValidation.Finite(
                multiplierIncrement,
                nameof(multiplierIncrement));
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
        "Critical Multiplier",
        typeof(CritMultiplierStatModifierParameters))]
    public sealed class CritMultiplierStatModifier : StaticStatModifier
    {
        public const uint ModifierIdValue = 0x01000006u;

        public CritMultiplierStatModifier(CritMultiplierStatModifierParameters parameters)
            : base(new EquipmentModifierID(ModifierIdValue), Validate(parameters))
        {
        }

        public override void Apply(AttackStatsMultipliers multipliers)
        {
            if (multipliers == null) throw new ArgumentNullException(nameof(multipliers));
            multipliers.critDamage +=
                ((CritMultiplierStatModifierParameters)Parameters).multiplierIncrement;
        }

        private static CritMultiplierStatModifierParameters Validate(
            CritMultiplierStatModifierParameters parameters)
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            NumericModifierValidation.Finite(parameters.multiplierIncrement, nameof(parameters));
            return parameters;
        }
    }
}
