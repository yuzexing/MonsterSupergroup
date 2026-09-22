using System;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Target intent is independent of the simulator's epoch and action checkpoint.</summary>
    [Serializable]
    public struct EnemyTargetState
    {
        public uint Revision;
        public uint AggroPlayerId;
        public uint ControllerPlayerId;
        public bool AllureControlled;
        public uint DecoyOwnerPlayerId;
        public ulong DecoyCastId;
        public Vector2 DecoyPosition;
        public double DecoyExpiresAt;

        public bool HasDecoy => DecoyOwnerPlayerId != 0 && DecoyCastId != 0;
        public bool MatchesDecoy(uint owner, ulong cast) => HasDecoy && DecoyOwnerPlayerId == owner && DecoyCastId == cast;
        public bool NeedsHandoff(EnemySimulationAssignment assignment) => AllureControlled &&
            assignment.Host != EnemySimulationHost.ServerAuthoritative &&
            (assignment.Host != EnemySimulationHost.ClientPlayer || assignment.SimulationOwnerPlayerId != AggroPlayerId);
    }
}
