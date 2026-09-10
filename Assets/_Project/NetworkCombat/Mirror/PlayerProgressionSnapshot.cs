using System;
using MonsterSupergroup.Gameplay.Combat;
using AstralShift.HellMaiden.Data.Perks;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Server-owned progression retained while an avatar is absent. Contains no runtime handles.</summary>
    [Serializable]
    public sealed class PlayerProgressionSnapshot
    {
        public int Level = 1;
        public float Experience;
        public int PendingUpgradeCount;
        public PendingUpgradeReward[] Rewards = Array.Empty<PendingUpgradeReward>();
        public UpgradeSelectionStage Stage;
        public uint SelectedEquipmentId;
        public PlayerUpgradeOfferSnapshot[] OriginalOffers = Array.Empty<PlayerUpgradeOfferSnapshot>();
        public uint BuildRevision;
        public uint OfferSequence;
        public PlayerUpgradeOfferSnapshot[] Offers = Array.Empty<PlayerUpgradeOfferSnapshot>();

        public PlayerProgressionSnapshot Copy() => new PlayerProgressionSnapshot
        {
            Level = Level,
            Experience = Experience,
            PendingUpgradeCount = PendingUpgradeCount,
            Rewards = (PendingUpgradeReward[])Rewards.Clone(),
            Stage = Stage,
            SelectedEquipmentId = SelectedEquipmentId,
            OriginalOffers = (PlayerUpgradeOfferSnapshot[])OriginalOffers.Clone(),
            BuildRevision = BuildRevision,
            OfferSequence = OfferSequence,
            Offers = (PlayerUpgradeOfferSnapshot[])Offers.Clone()
        };
    }

    [Serializable]
    public struct PlayerUpgradeOfferSnapshot
    {
        public ulong PreviousOfferId;
        public UpgradeRewardKind Kind;
        public uint ContentId;
        public PerkRarity Rarity;
        public int PerkLevel;
        public uint EquipmentId;
        public int LevelIndex;
        public int SlotIndex;
        public bool UpgradesExistingEquipment;
    }
}
