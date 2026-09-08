using System;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Server-owned progression retained while an avatar is absent. Contains no runtime handles.</summary>
    [Serializable]
    public sealed class PlayerProgressionSnapshot
    {
        public int Level = 1;
        public float Experience;
        public int PendingUpgradeCount;
        public uint BuildRevision;
        public uint OfferSequence;
        public PlayerUpgradeOfferSnapshot[] Offers = Array.Empty<PlayerUpgradeOfferSnapshot>();

        public PlayerProgressionSnapshot Copy() => new PlayerProgressionSnapshot
        {
            Level = Level,
            Experience = Experience,
            PendingUpgradeCount = PendingUpgradeCount,
            BuildRevision = BuildRevision,
            OfferSequence = OfferSequence,
            Offers = (PlayerUpgradeOfferSnapshot[])Offers.Clone()
        };
    }

    [Serializable]
    public struct PlayerUpgradeOfferSnapshot
    {
        public ulong PreviousOfferId;
        public uint EquipmentId;
        public int LevelIndex;
        public int SlotIndex;
        public bool UpgradesExistingEquipment;
    }
}
