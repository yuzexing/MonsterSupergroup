using System;

namespace MonsterSupergroup.GAS
{
    [Serializable]
    public sealed class CritRateStatModifierParameters : EquipmentModifierParameters
    {
        public CritRateStatModifierParameters()
        {
        }

        public CritRateStatModifierParameters(float multiplierIncrement)
        {
            this.multiplierIncrement = NumericModifierValidation.Finite(
                multiplierIncrement,
                nameof(multiplierIncrement));
        }

        public float multiplierIncrement;
        public float MultiplierIncrement => multiplierIncrement;
    }

    [EquipmentModifierType(ModifierIdValue, "Critical Rate", typeof(CritRateStatModifierParameters))]
    public sealed class CritRateStatModifier : StaticStatModifier
    {
        public const uint ModifierIdValue = 0x01000005u;

        public CritRateStatModifier(CritRateStatModifierParameters parameters)
            : base(new EquipmentModifierID(ModifierIdValue), Validate(parameters))
        {
        }

        public override void Apply(AttackStatsMultipliers multipliers)
        {
            if (multipliers == null) throw new ArgumentNullException(nameof(multipliers));
            multipliers.critRate += ((CritRateStatModifierParameters)Parameters).multiplierIncrement;
        }

        private static CritRateStatModifierParameters Validate(CritRateStatModifierParameters parameters)
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            NumericModifierValidation.Finite(parameters.multiplierIncrement, nameof(parameters));
            return parameters;
        }
    }
}
