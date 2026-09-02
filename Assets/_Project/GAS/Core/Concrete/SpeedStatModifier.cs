using System;

namespace MonsterSupergroup.GAS
{
    [Serializable]
    public sealed class SpeedStatModifierParameters : EquipmentModifierParameters
    {
        public SpeedStatModifierParameters()
        {
        }

        public SpeedStatModifierParameters(float multiplierIncrement)
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

    [EquipmentModifierType(ModifierIdValue, "Speed", typeof(SpeedStatModifierParameters))]
    public sealed class SpeedStatModifier : StaticStatModifier
    {
        public const uint ModifierIdValue = 0x01000002u;

        public SpeedStatModifier(SpeedStatModifierParameters parameters)
            : base(new EquipmentModifierID(ModifierIdValue), Validate(parameters))
        {
        }

        public override void Apply(AttackStatsMultipliers multipliers)
        {
            if (multipliers == null) throw new ArgumentNullException(nameof(multipliers));
            multipliers.speed += ((SpeedStatModifierParameters)Parameters).multiplierIncrement;
        }

        private static SpeedStatModifierParameters Validate(SpeedStatModifierParameters parameters)
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            NumericModifierValidation.Finite(parameters.multiplierIncrement, nameof(parameters));
            return parameters;
        }
    }
}
