using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Data.Cards;

namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>A selectable card level, not an instantiated gameplay modifier.</summary>
    public sealed class ModifierOffer
    {
        internal ModifierOffer(ulong offerId, EquipmentData equipment, int levelIndex)
        {
            OfferId = offerId;
            Equipment = equipment;
            LevelIndex = levelIndex;
            Modifiers = Array.AsReadOnly(equipment.Levels[levelIndex].Modifiers);
        }

        public ulong OfferId { get; }
        public EquipmentData Equipment { get; }
        public uint EquipmentId => Equipment.ID;
        public int LevelIndex { get; }
        public IReadOnlyList<EquipmentModifierApplication> Modifiers { get; }
        // Equipment also supplies GetDescription(level) and VisualDataReference.
        public string DisplayName => Equipment.GetTitle();
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
