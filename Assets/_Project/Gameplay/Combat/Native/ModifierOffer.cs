using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Data.Perks;

namespace MonsterSupergroup.Gameplay.Combat
{
    public enum UpgradeRewardKind : byte { Equipment, Weapon, Perk }
    public enum UpgradeSelectionStage : byte { Reward, EquipmentTarget }

    [Serializable]
    public struct PendingUpgradeReward
    {
        public int EarnedLevel;
        public UpgradeRewardKind Kind;
    }

    /// <summary>A selectable card level, not an instantiated gameplay modifier.</summary>
    public sealed class ModifierOffer
    {
        private ModifierOffer(ulong id, UpgradeRewardKind kind, EquipmentData equipment,
            WeaponData weapon, PerkData perk, PerkRarity rarity, int perkLevel)
        {
            OfferId = id;
            Kind = kind;
            Equipment = equipment;
            Weapon = weapon;
            Perk = perk;
            Rarity = rarity;
            PerkLevel = perkLevel;
            LevelIndex = -1;
            TargetSlotIndex = -1;
            Modifiers = Array.Empty<EquipmentModifierApplication>();
        }

        public static ModifierOffer EquipmentCard(ulong id, EquipmentData data) =>
            new ModifierOffer(id, UpgradeRewardKind.Equipment, data ?? throw new ArgumentNullException(nameof(data)), null, null, default, 0);
        public static ModifierOffer WeaponCard(ulong id, WeaponData data) =>
            new ModifierOffer(id, UpgradeRewardKind.Weapon, null, data ?? throw new ArgumentNullException(nameof(data)), null, default, 0);
        public static ModifierOffer PerkCard(ulong id, PerkData data, PerkRarity rarity, int level) =>
            new ModifierOffer(id, UpgradeRewardKind.Perk, null, null, data ?? throw new ArgumentNullException(nameof(data)), rarity, level);

        public ModifierOffer WithId(ulong id)
        {
            if (Kind == UpgradeRewardKind.Weapon) return WeaponCard(id, Weapon);
            if (Kind == UpgradeRewardKind.Perk) return PerkCard(id, Perk, Rarity, PerkLevel);
            return TargetSlotIndex < 0 ? EquipmentCard(id, Equipment) :
                new ModifierOffer(id, Equipment, LevelIndex, TargetSlotIndex, ExistingEquipmentHandle);
        }

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
        public UpgradeRewardKind Kind { get; }
        public WeaponData Weapon { get; }
        public PerkData Perk { get; }
        public PerkRarity Rarity { get; }
        public int PerkLevel { get; }
        public uint ContentId => Kind == UpgradeRewardKind.Weapon ? Weapon.ID :
            Kind == UpgradeRewardKind.Perk ? Perk.ID : Equipment.ID;
        public EquipmentData Equipment { get; }
        public uint EquipmentId => Equipment != null ? Equipment.ID : 0;
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
                string localized = Kind == UpgradeRewardKind.Weapon ? Weapon.GetTitle() :
                    Kind == UpgradeRewardKind.Perk ? Perk.GetTitle() : Equipment.GetTitle();
                string title = Kind == UpgradeRewardKind.Weapon ? Weapon.Title :
                    Kind == UpgradeRewardKind.Perk ? Perk.Title : Equipment.Title;
                return string.IsNullOrWhiteSpace(localized) ? title : localized;
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
