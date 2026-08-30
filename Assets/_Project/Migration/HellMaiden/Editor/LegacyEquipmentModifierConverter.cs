using System;
using System.Reflection;
using MonsterSupergroup.GAS;
using LegacyEquipmentDataModifier = AstralShift.HellMaiden.Data.EquipmentDataModifier;
using NativeEquipmentDataModifier = MonsterSupergroup.GAS.Authoring.EquipmentDataModifier;

namespace MonsterSupergroup.HellMaidenMigration.Editor
{
    /// <summary>
    /// The only boundary that understands HellMaiden's unstable/hash-derived modifier IDs.
    /// Converted assets and runtime code only retain New GAS stable IDs and typed parameters.
    /// </summary>
    public static class LegacyEquipmentModifierConverter
    {
        public const uint LegacyDamageId = 1120648u;
        public const uint LegacySpeedId = 3809246214u;
        public const uint LegacySizeId = 1050114896u;
        public const uint LegacyDurationId = 19982737u;
        public const uint LegacyCritRateId = 3443713987u;
        public const uint LegacyCritMultiplierId = 2717296302u;
        public const uint LegacyProjectileCountId = 1251233216u;
        public const uint LegacyKnockbackId = 3977118250u;

        public static NativeEquipmentDataModifier Convert(
            LegacyEquipmentDataModifier source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source.ModifierID == null)
            {
                throw new InvalidOperationException("Legacy modifier has no ID.");
            }

            if (source.HasMultiSlotConfig)
            {
                throw new NotSupportedException(
                    "The first Dante migration slice does not include legacy multi-slot rules.");
            }

            return Convert(
                source.ModifierID.Value,
                ReadNumericParameter(source.ModifierID.Value, source.Parameters));
        }

        public static NativeEquipmentDataModifier Convert(
            uint legacyModifierId,
            float firstNumericParameter)
        {
            if (float.IsNaN(firstNumericParameter) ||
                float.IsInfinity(firstNumericParameter))
            {
                throw new ArgumentOutOfRangeException(nameof(firstNumericParameter));
            }

            switch (legacyModifierId)
            {
                case LegacyDamageId:
                    return Create(
                        DamageStatModifier.ModifierIdValue,
                        new DamageStatModifierParameters(firstNumericParameter));
                case LegacySpeedId:
                    return Create(
                        SpeedStatModifier.ModifierIdValue,
                        new SpeedStatModifierParameters(firstNumericParameter));
                case LegacySizeId:
                    return Create(
                        SizeStatModifier.ModifierIdValue,
                        new SizeStatModifierParameters(firstNumericParameter));
                case LegacyDurationId:
                    return Create(
                        DurationStatModifier.ModifierIdValue,
                        new DurationStatModifierParameters(firstNumericParameter));
                case LegacyCritRateId:
                    return Create(
                        CritRateStatModifier.ModifierIdValue,
                        new CritRateStatModifierParameters(firstNumericParameter));
                case LegacyCritMultiplierId:
                    return Create(
                        CritMultiplierStatModifier.ModifierIdValue,
                        new CritMultiplierStatModifierParameters(firstNumericParameter));
                case LegacyProjectileCountId:
                    return Create(
                        ProjectileCountStatModifier.ModifierIdValue,
                        new ProjectileCountStatModifierParameters(
                            RequireWholeNumber(firstNumericParameter)));
                case LegacyKnockbackId:
                    return Create(
                        KnockbackStatModifier.ModifierIdValue,
                        new KnockbackStatModifierParameters(firstNumericParameter));
                default:
                    throw new NotSupportedException(
                        $"Legacy modifier ID {legacyModifierId} is outside the Dante numeric slice.");
            }
        }

        private static NativeEquipmentDataModifier Create(
            uint stableId,
            EquipmentModifierParameters parameters)
        {
            return new NativeEquipmentDataModifier(
                new EquipmentModifierID(stableId),
                parameters);
        }

        private static int RequireWholeNumber(float value)
        {
            int result = checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
            if (Math.Abs(value - result) > 0.0001f)
            {
                throw new InvalidOperationException(
                    $"Projectile count increment must be a whole number, got {value}.");
            }

            return result;
        }

        private static float ReadNumericParameter(uint legacyModifierId, object parameters)
        {
            if (parameters == null)
            {
                throw new InvalidOperationException(
                    $"Legacy modifier ID {legacyModifierId} has no parameter object.");
            }

            string fieldName = legacyModifierId == LegacyProjectileCountId
                ? "countIncrement"
                : "multiplierIncrement";
            FieldInfo field = parameters.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException(
                    $"Legacy modifier ID {legacyModifierId} has no '{fieldName}' parameter.");
            }

            object value = field.GetValue(parameters);
            switch (value)
            {
                case int integer:
                    return integer;
                case float single:
                    return single;
                case double doubleValue:
                    return checked((float)doubleValue);
                default:
                    throw new InvalidOperationException(
                        $"Legacy parameter '{fieldName}' is not numeric.");
            }
        }
    }
}
