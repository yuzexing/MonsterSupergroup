using System;
using AstralShift.HellMaiden.Data;
using MonsterSupergroup.GAS;
using UnityEngine;
using NativeEquipmentDataModifier =
    MonsterSupergroup.GAS.Authoring.EquipmentDataModifier;

namespace AstralShift.HellMaiden.Data.Cards
{
    /// <summary>
    /// Canonical card-level authoring record for one New GAS equipment modifier.
    /// The modifier definition owns combat data; the application fields preserve
    /// HellMaiden's hand-slot targeting semantics without reviving its runtime.
    /// </summary>
    [Serializable]
    public sealed class EquipmentModifierApplication
    {
        [SerializeField] private NativeEquipmentDataModifier modifier;
        [SerializeField] private string descriptionToken;
        [SerializeField] private bool multiSlotConfig;
        [SerializeField] private EquipmentMultiSlotConfig multiSlot =
            new EquipmentMultiSlotConfig();

        public NativeEquipmentDataModifier Modifier => modifier;
        public MonsterSupergroup.GAS.EquipmentModifierID ModifierId =>
            modifier?.ModifierId ?? default;
        public uint ModifierIdValue => modifier?.ModifierIdValue ?? 0u;
        public EquipmentModifierParameters Parameters => modifier?.Parameters;
        public string DescriptionToken => descriptionToken ?? string.Empty;
        public bool HasMultiSlotConfig => multiSlotConfig;
        public EquipmentMultiSlotConfig MultiSlot => multiSlot;

        public float GetParameterByIndex(int index)
        {
            if (modifier?.Parameters == null)
            {
                throw new InvalidOperationException(
                    "Equipment modifier application has no typed parameters.");
            }

            if (!modifier.Parameters.TryGetNumericParameter(index, out float value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    $"Parameter {index} on modifier 0x{modifier.ModifierIdValue:X8} " +
                    "is not exposed as a numeric description parameter.");
            }

            return value;
        }

        public void Configure(
            NativeEquipmentDataModifier newModifier,
            string newDescriptionToken,
            bool hasMultiSlotConfig,
            EquipmentMultiSlotConfig newMultiSlot)
        {
            modifier = newModifier ?? throw new ArgumentNullException(nameof(newModifier));
            descriptionToken = newDescriptionToken ?? string.Empty;
            multiSlotConfig = hasMultiSlotConfig;
            multiSlot = newMultiSlot ?? new EquipmentMultiSlotConfig();
        }
    }
}
