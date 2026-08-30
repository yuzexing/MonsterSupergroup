using System;

namespace MonsterSupergroup.GAS
{
    [Serializable]
    public sealed class DurationStatModifierParameters : EquipmentModifierParameters
    {
        public DurationStatModifierParameters()
        {
        }

        public DurationStatModifierParameters(float multiplierIncrement)
        {
            this.multiplierIncrement = NumericModifierValidation.Finite(
                multiplierIncrement,
                nameof(multiplierIncrement));
        }

        public float multiplierIncrement;
        public float MultiplierIncrement => multiplierIncrement;
    }

    [EquipmentModifierType(ModifierIdValue, "Duration", typeof(DurationStatModifierParameters))]
    public sealed class DurationStatModifier : StaticStatModifier
    {
        public const uint ModifierIdValue = 0x01000004u;

        public DurationStatModifier(DurationStatModifierParameters parameters)
            : base(new EquipmentModifierID(ModifierIdValue), Validate(parameters))
        {
        }

        public override void Apply(AttackStatsMultipliers multipliers)
        {
            if (multipliers == null) throw new ArgumentNullException(nameof(multipliers));
            multipliers.duration += ((DurationStatModifierParameters)Parameters).multiplierIncrement;
        }

        private static DurationStatModifierParameters Validate(DurationStatModifierParameters parameters)
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            NumericModifierValidation.Finite(parameters.multiplierIncrement, nameof(parameters));
            return parameters;
        }
    }
}
