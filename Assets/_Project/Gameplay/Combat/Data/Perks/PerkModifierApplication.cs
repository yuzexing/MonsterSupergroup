using System;
using MonsterSupergroup.GAS;
using UnityEngine;
using NativePerkDataModifier = MonsterSupergroup.GAS.Authoring.PerkDataModifier;

namespace AstralShift.HellMaiden.Data.Perks
{
    public enum PerkApplicationDomain
    {
        WeaponStats = 0,
        PlayerAttributes = 1,
        ConditionalCombat = 2,
        EquipmentTriggers = 3,
        Reward = 4,
        RunState = 5
    }

    /// <summary>
    /// Canonical Perk authoring record. PerkData keeps HellMaiden's card and
    /// rarity structure while every executable modifier is a stable New GAS
    /// definition routed to an explicit owner-scoped domain.
    /// </summary>
    [Serializable]
    public sealed class PerkModifierApplication
    {
        [SerializeField] private NativePerkDataModifier modifier;
        [SerializeField] private PerkApplicationDomain domain;
        [SerializeField] private string descriptionToken;

        public NativePerkDataModifier Modifier => modifier;
        public MonsterSupergroup.GAS.PerkModifierID ModifierId =>
            modifier?.ModifierId ?? default;
        public uint ModifierIdValue => modifier?.ModifierIdValue ?? 0u;
        public PerkModifierParameters Parameters => modifier?.Parameters;
        public PerkApplicationDomain Domain => domain;
        public string DescriptionToken => descriptionToken ?? string.Empty;

        public float GetParameterByIndex(int index)
        {
            if (modifier?.Parameters == null)
            {
                throw new InvalidOperationException(
                    "Perk modifier application has no typed parameters.");
            }

            if (!modifier.Parameters.TryGetNumericParameter(index, out float value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    $"Parameter {index} on perk modifier " +
                    $"0x{modifier.ModifierIdValue:X8} is not exposed as numeric.");
            }

            return value;
        }

        public void Configure(
            NativePerkDataModifier newModifier,
            PerkApplicationDomain newDomain,
            string newDescriptionToken)
        {
            modifier = newModifier ?? throw new ArgumentNullException(nameof(newModifier));
            domain = newDomain;
            descriptionToken = newDescriptionToken ?? string.Empty;
        }
    }
}
