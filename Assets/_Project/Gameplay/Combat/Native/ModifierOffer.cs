using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Data.Cards;

namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>A selectable card level, not an instantiated gameplay modifier.</summary>
    public sealed class ModifierOffer
    {
        public ModifierOffer(ulong offerId, EquipmentData equipment, int levelIndex,
            int targetSlotIndex = 0, PlayerBuildEquipmentHandle existingEquipmentHandle = default)
        {
            if (equipment == null) throw new ArgumentNullException(nameof(equipment));
            if (equipment.Levels == null || (uint)levelIndex >= equipment.Levels.Length ||
                equipment.Levels[levelIndex] == null)
                throw new ArgumentOutOfRangeException(nameof(levelIndex));
            if ((uint)targetSlotIndex >= PlayerBuildRuntime.HandSlotCount)
                throw new ArgumentOutOfRangeException(nameof(targetSlotIndex));
            OfferId = offerId;
            Equipment = equipment;
            LevelIndex = levelIndex;
            TargetSlotIndex = targetSlotIndex;
            ExistingEquipmentHandle = existingEquipmentHandle;
            Modifiers = Array.AsReadOnly(equipment.Levels[levelIndex].Modifiers);
        }

        public ulong OfferId { get; }
        public EquipmentData Equipment { get; }
        public uint EquipmentId => Equipment.ID;
        public int LevelIndex { get; }
        public int TargetSlotIndex { get; }
        // Local authoritative handle only; transport carries card/level/slot IDs.
        public PlayerBuildEquipmentHandle ExistingEquipmentHandle { get; }
        public IReadOnlyList<EquipmentModifierApplication> Modifiers { get; }
        // Equipment also supplies GetDescription(level) and VisualDataReference.
        public string DisplayName
        {
            get
            {
                string localized = Equipment.GetTitle();
                return string.IsNullOrWhiteSpace(localized) ? Equipment.Title : localized;
            }
        }
    }

    public readonly struct ModifierSelectionResult
    {
        private ModifierSelectionResult(bool succeeded, PlayerBuildEquipmentHandle handle, string error)
        {
            Succeeded = succeeded;
            EquipmentHandle = handle;
            Error = error;
        }

        public bool Succeeded { get; }
        public PlayerBuildEquipmentHandle EquipmentHandle { get; }
        public string Error { get; }

        internal static ModifierSelectionResult Success(PlayerBuildEquipmentHandle handle) =>
            new ModifierSelectionResult(true, handle, null);

        internal static ModifierSelectionResult Failure(string error) =>
            new ModifierSelectionResult(false, default, error);
    }
}
