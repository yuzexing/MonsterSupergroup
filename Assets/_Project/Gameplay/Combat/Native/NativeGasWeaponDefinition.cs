using System;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using UnityEngine;
using GasAttackStats = MonsterSupergroup.GAS.AttackStats;

namespace MonsterSupergroup.Gameplay.Combat
{
    [CreateAssetMenu(
        fileName = "NativeGasWeapon",
        menuName = "Monster Supergroup/GAS/Native HellMaiden Weapon")]
    public sealed class NativeGasWeaponDefinition : ScriptableObject
    {
        [SerializeField] private uint combatId = 1;
        [SerializeField] private GasAttackStats baseStats = new GasAttackStats
        {
            damage = 10,
            critMultiplier = 1.5f,
            critRate = 0.1f,
            speed = 1f,
            size = 1f,
            duration = 1f,
            projectileCount = 1
        };
        [SerializeField] private long attackTagsValue =
            (long)(CombatTags.Attack | CombatTags.Projectile);
        [SerializeField] private ModifierFlags supportedModifiers =
            ModifierFlags.Damage |
            ModifierFlags.Size |
            ModifierFlags.Speed |
            ModifierFlags.Duration |
            ModifierFlags.ProjectileCount |
            ModifierFlags.CritRate |
            ModifierFlags.CritDamage |
            ModifierFlags.KnockBack;
        [SerializeField] private KnockbackSettings knockbackPresentation;

        public uint CombatId => combatId;
        public GasAttackStats BaseStats => baseStats;
        public CombatTags AttackTags =>
            (CombatTags)(ulong)attackTagsValue | CombatTags.Attack;
        public ModifierFlags SupportedModifiers => supportedModifiers;
        public KnockbackSettings KnockbackPresentation => knockbackPresentation;

        public bool Supports(MonsterSupergroup.GAS.EquipmentModifierID modifierId)
        {
            ModifierFlags requiredFlag;
            switch (modifierId.Value)
            {
                case DamageStatModifier.ModifierIdValue:
                    requiredFlag = ModifierFlags.Damage;
                    break;
                case SpeedStatModifier.ModifierIdValue:
                    requiredFlag = ModifierFlags.Speed;
                    break;
                case SizeStatModifier.ModifierIdValue:
                    requiredFlag = ModifierFlags.Size;
                    break;
                case DurationStatModifier.ModifierIdValue:
                    requiredFlag = ModifierFlags.Duration;
                    break;
                case CritRateStatModifier.ModifierIdValue:
                    requiredFlag = ModifierFlags.CritRate;
                    break;
                case CritMultiplierStatModifier.ModifierIdValue:
                    requiredFlag = ModifierFlags.CritDamage;
                    break;
                case ProjectileCountStatModifier.ModifierIdValue:
                    requiredFlag = ModifierFlags.ProjectileCount;
                    break;
                case KnockbackStatModifier.ModifierIdValue:
                    requiredFlag = ModifierFlags.KnockBack;
                    break;
                default:
                    return true;
            }

            return (supportedModifiers & requiredFlag) != 0;
        }

        public void Configure(
            uint newCombatId,
            GasAttackStats newBaseStats,
            CombatTags newAttackTags,
            ModifierFlags newSupportedModifiers,
            KnockbackSettings newKnockbackPresentation)
        {
            combatId = newCombatId;
            baseStats = newBaseStats;
            CombatTags normalizedTags = newAttackTags | CombatTags.Attack;
            if (((ulong)normalizedTags & 0x8000000000000000UL) != 0UL)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(newAttackTags),
                    "Serialized combat tags must fit in a signed 64-bit value.");
            }

            attackTagsValue = (long)(ulong)normalizedTags;
            supportedModifiers = newSupportedModifiers;
            knockbackPresentation = newKnockbackPresentation;
            Validate();
        }

        public void Validate()
        {
            if (combatId == 0u)
            {
                throw new InvalidOperationException($"{name} has a zero Combat ID.");
            }

            if (baseStats.damage < 0 || baseStats.projectileCount < 1 ||
                !FiniteNonNegative(baseStats.critMultiplier) ||
                !FiniteNonNegative(baseStats.critRate) ||
                !FinitePositive(baseStats.speed) ||
                !FiniteNonNegative(baseStats.size) ||
                !FiniteNonNegative(baseStats.duration) ||
                !FiniteNonNegative(baseStats.knockbackDistance))
            {
                throw new InvalidOperationException($"{name} contains invalid base attack stats.");
            }
        }

        private static bool FinitePositive(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool FiniteNonNegative(float value) =>
            value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
