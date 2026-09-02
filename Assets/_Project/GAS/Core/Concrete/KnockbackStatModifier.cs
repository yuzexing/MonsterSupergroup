using System;

namespace MonsterSupergroup.GAS
{
    [Serializable]
    public sealed class KnockbackStatModifierParameters : EquipmentModifierParameters
    {
        public KnockbackStatModifierParameters()
        {
        }

        public KnockbackStatModifierParameters(float multiplierIncrement)
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
        "Knockback",
        typeof(KnockbackStatModifierParameters))]
    public sealed class KnockbackStatModifier : StaticStatModifier
    {
        public const uint ModifierIdValue = 0x01000008u;

        public KnockbackStatModifier(KnockbackStatModifierParameters parameters)
            : base(new EquipmentModifierID(ModifierIdValue), Validate(parameters))
        {
        }

        public override void Apply(AttackStatsMultipliers multipliers)
        {
            if (multipliers == null) throw new ArgumentNullException(nameof(multipliers));
            multipliers.knockBackMultiplier +=
                ((KnockbackStatModifierParameters)Parameters).multiplierIncrement;
        }

        private static KnockbackStatModifierParameters Validate(
            KnockbackStatModifierParameters parameters)
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            NumericModifierValidation.Finite(parameters.multiplierIncrement, nameof(parameters));
            return parameters;
        }
    }
}
