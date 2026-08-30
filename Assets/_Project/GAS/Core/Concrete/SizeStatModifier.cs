using System;

namespace MonsterSupergroup.GAS
{
    [Serializable]
    public sealed class SizeStatModifierParameters : EquipmentModifierParameters
    {
        public SizeStatModifierParameters()
        {
        }

        public SizeStatModifierParameters(float multiplierIncrement)
        {
            this.multiplierIncrement = NumericModifierValidation.Finite(
                multiplierIncrement,
                nameof(multiplierIncrement));
        }

        public float multiplierIncrement;
        public float MultiplierIncrement => multiplierIncrement;
    }

    [EquipmentModifierType(ModifierIdValue, "Size", typeof(SizeStatModifierParameters))]
    public sealed class SizeStatModifier : StaticStatModifier
    {
        public const uint ModifierIdValue = 0x01000003u;

        public SizeStatModifier(SizeStatModifierParameters parameters)
            : base(new EquipmentModifierID(ModifierIdValue), Validate(parameters))
        {
        }

        public override void Apply(AttackStatsMultipliers multipliers)
        {
            if (multipliers == null) throw new ArgumentNullException(nameof(multipliers));
            multipliers.size += ((SizeStatModifierParameters)Parameters).multiplierIncrement;
        }

        private static SizeStatModifierParameters Validate(SizeStatModifierParameters parameters)
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            NumericModifierValidation.Finite(parameters.multiplierIncrement, nameof(parameters));
            return parameters;
        }
    }
}
