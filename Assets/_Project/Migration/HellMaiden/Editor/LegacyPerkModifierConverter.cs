using System;
using MonsterSupergroup.GAS;
using NativePerkDataModifier = MonsterSupergroup.GAS.Authoring.PerkDataModifier;

namespace MonsterSupergroup.HellMaidenMigration.Editor
{
    /// <summary>
    /// Editor-only boundary from HellMaiden hash IDs to stable New GAS perk IDs.
    /// Runtime assets and PlayerBuildRuntime never read the legacy IDs.
    /// </summary>
    public static class LegacyPerkModifierConverter
    {
        public const uint LegacyWeaponDamageId = 2165472666u;
        public const uint LegacyWeaponSpeedId = 3372100348u;
        public const uint LegacyWeaponSizeId = 2550586810u;
        public const uint LegacyWeaponDurationId = 3102792455u;
        public const uint LegacyWeaponCritRateId = 1898389605u;
        public const uint LegacyWeaponCritMultiplierId = 1154401916u;
        public const uint LegacyProjectileCountId = 2441468182u;

        public static NativePerkDataModifier Convert(
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
                case LegacyWeaponDamageId:
                    return Create(
                        WeaponDamagePerkModifier.ModifierIdValue,
                        new WeaponDamagePerkModifierParameters(firstNumericParameter));
                case LegacyWeaponSpeedId:
                    return Create(
                        WeaponSpeedPerkModifier.ModifierIdValue,
                        new WeaponSpeedPerkModifierParameters(firstNumericParameter));
                case LegacyWeaponSizeId:
                    return Create(
                        WeaponSizePerkModifier.ModifierIdValue,
                        new WeaponSizePerkModifierParameters(firstNumericParameter));
                case LegacyWeaponDurationId:
                    return Create(
                        WeaponDurationPerkModifier.ModifierIdValue,
                        new WeaponDurationPerkModifierParameters(firstNumericParameter));
                case LegacyWeaponCritRateId:
                    return Create(
                        WeaponCritRatePerkModifier.ModifierIdValue,
                        new WeaponCritRatePerkModifierParameters(firstNumericParameter));
                case LegacyWeaponCritMultiplierId:
                    return Create(
                        WeaponCritMultiplierPerkModifier.ModifierIdValue,
                        new WeaponCritMultiplierPerkModifierParameters(
                            firstNumericParameter));
                case LegacyProjectileCountId:
                    return Create(
                        WeaponProjectileCountPerkModifier.ModifierIdValue,
                        new WeaponProjectileCountPerkModifierParameters(
                            RequireWholeNumber(firstNumericParameter)));
                default:
                    throw new NotSupportedException(
                        $"Legacy perk modifier ID {legacyModifierId} is not a " +
                        "migrated pure weapon-stat modifier.");
            }
        }

        private static NativePerkDataModifier Create(
            uint stableId,
            PerkModifierParameters parameters)
        {
            return new NativePerkDataModifier(
                new PerkModifierID(stableId),
                parameters);
        }

        private static int RequireWholeNumber(float value)
        {
            int result = checked((int)Math.Round(
                value,
                MidpointRounding.AwayFromZero));
            if (Math.Abs(value - result) > 0.0001f)
            {
                throw new InvalidOperationException(
                    $"Projectile count increment must be a whole number, got {value}.");
            }

            return result;
        }
    }
}
