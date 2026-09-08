using System;
using AstralShift.HellMaiden.Data.Perks;

namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>Definition data for rebuilding the existing GAS runtime, never a second runtime.</summary>
    [Serializable]
    public sealed class PlayerBuildSnapshot
    {
        public uint InitialWeaponId;
        public int InitialWeaponSlot = -1;
        public PlayerBuildWeaponSnapshot[] Weapons = Array.Empty<PlayerBuildWeaponSnapshot>();
        public PlayerBuildEquipmentSnapshot[] Equipment = Array.Empty<PlayerBuildEquipmentSnapshot>();
        public PlayerBuildPerkSnapshot[] Perks = Array.Empty<PlayerBuildPerkSnapshot>();

        public PlayerBuildSnapshot Copy() => new PlayerBuildSnapshot
        {
            InitialWeaponId = InitialWeaponId,
            InitialWeaponSlot = InitialWeaponSlot,
            Weapons = (PlayerBuildWeaponSnapshot[])Weapons.Clone(),
            Equipment = (PlayerBuildEquipmentSnapshot[])Equipment.Clone(),
            Perks = (PlayerBuildPerkSnapshot[])Perks.Clone()
        };
    }

    [Serializable]
    public struct PlayerBuildWeaponSnapshot
    {
        public int SlotIndex;
        public uint WeaponId;
    }

    [Serializable]
    public struct PlayerBuildEquipmentSnapshot
    {
        public int SlotIndex;
        public uint EquipmentId;
        public int LevelIndex;
    }

    [Serializable]
    public struct PlayerBuildPerkSnapshot
    {
        public uint PerkId;
        // Existing Perk Runtime uses authored rarity, rather than a separate numeric level.
        public PerkRarity Rarity;
    }
}
