using System;
using AstralShift.HellMaiden.Player.Attacks;

namespace MonsterSupergroup.NetworkCombat
{
    [Serializable]
    public struct NetworkUltimateState
    {
        public uint Revision;
        public MonsterSupergroup.Gameplay.Combat.PlayerUltimateSnapshot Snapshot;
    }

    [Serializable]
    public struct NetworkUltimatePresentationSpawn
    {
        public uint SourcePlayerId, UltimateId;
        public ulong AttackEventId;
        public double EventNetworkTime;
        public ProjectilePresentationStats Stats;
        public AttackElement Element;
        public bool IsValid => SourcePlayerId != 0 && AttackEventId != 0 &&
            (UltimateId & 0x80000000u) == 0 && Stats.IsFinite && Element == AttackElement.Default &&
            NetworkSummonPresentationState.FiniteTime(EventNetworkTime);
        public UltimatePresentationSpawn ToSpawn() => new UltimatePresentationSpawn(UltimateId, AttackEventId, Stats, Element);
    }
}
