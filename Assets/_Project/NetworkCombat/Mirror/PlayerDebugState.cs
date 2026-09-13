using System;
using MonsterSupergroup.Gameplay.Combat;

namespace MonsterSupergroup.NetworkCombat
{
    [Serializable]
    public struct PlayerProgressionDebugState : IEquatable<PlayerProgressionDebugState>
    {
        public int PendingUpgradeCount;
        public UpgradeSelectionStage Stage;
        public int OfferedLevel;
        public uint BuildRevision;
        public bool BuildReady;
        public bool IsSelecting;

        public bool Equals(PlayerProgressionDebugState other) =>
            PendingUpgradeCount == other.PendingUpgradeCount && Stage == other.Stage &&
            OfferedLevel == other.OfferedLevel && BuildRevision == other.BuildRevision &&
            BuildReady == other.BuildReady && IsSelecting == other.IsSelecting;
    }

    public struct PlayerUltimateDebugState
    {
        public PlayerUltimateSnapshot State;
        public uint Revision;
        public bool ExecutionEnabled;
        public bool PendingUse;
    }
}
