using System;
using AstralShift.HellMaiden.AI;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public static class EnemySimulationSequence
    {
        public static bool IsNewer(uint candidate, uint previous)
        {
            if (candidate == 0u || candidate == previous)
            {
                return false;
            }

            return previous == 0u ||
                unchecked(candidate - previous) < 0x80000000u;
        }
    }

    public enum EnemySimulationHost : byte
    {
        Frozen = 0,
        ClientPlayer = 1,
        ServerFallback = 2,
        ServerAuthoritative = 3
    }

    [Flags]
    public enum EnemySimulationSnapshotFlags : byte
    {
        None = 0,
        Discontinuity = 1 << 0
    }

    [Serializable]
    public struct EnemySimulationAssignment : IEquatable<EnemySimulationAssignment>
    {
        public uint EnemyEntityId;
        public EnemySimulationHost Host;
        public uint SimulationOwnerPlayerId;
        public uint AggroTargetPlayerId;
        public uint Epoch;

        public bool Equals(EnemySimulationAssignment other)
        {
            return EnemyEntityId == other.EnemyEntityId &&
                Host == other.Host &&
                SimulationOwnerPlayerId == other.SimulationOwnerPlayerId &&
                AggroTargetPlayerId == other.AggroTargetPlayerId &&
                Epoch == other.Epoch;
        }

        public override bool Equals(object obj)
        {
            return obj is EnemySimulationAssignment other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)EnemyEntityId;
                hash = (hash * 397) ^ (int)Host;
                hash = (hash * 397) ^ (int)SimulationOwnerPlayerId;
                hash = (hash * 397) ^ (int)AggroTargetPlayerId;
                hash = (hash * 397) ^ (int)Epoch;
                return hash;
            }
        }
    }

    [Serializable]
    public struct EnemySimulationSnapshot
    {
        public uint EnemyEntityId;
        public uint AssignmentEpoch;
        public uint Sequence;
        public double SampleNetworkTime;
        public Vector2 Position;
        public Vector2 Velocity;
        public Vector2 Facing;
        public EnemySimulationSnapshotFlags Flags;

        public bool IsFinite =>
            IsFiniteValue(SampleNetworkTime) &&
            IsFiniteValue(Position.x) &&
            IsFiniteValue(Position.y) &&
            IsFiniteValue(Velocity.x) &&
            IsFiniteValue(Velocity.y) &&
            IsFiniteValue(Facing.x) &&
            IsFiniteValue(Facing.y);

        private static bool IsFiniteValue(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    [Serializable]
    public struct EnemySimulationSnapshotBatch
    {
        public uint BatchSequence;
        public EnemySimulationSnapshot[] Snapshots;
    }

    /// <summary>
    /// Reliable edge for the observer-facing portion of an Enemy attack. It is
    /// separate from the lossy Transform stream so dropping a movement packet
    /// cannot leave an observer's attack window in the wrong phase.
    /// </summary>
    [Serializable]
    public struct EnemyAttackPresentationEdge
    {
        public uint EnemyEntityId;
        public uint AssignmentEpoch;
        public uint StateSequence;
        public double StateStartNetworkTime;
        public float PhaseDuration;
        public EnemyAttackPresentationPhase Phase;
        public Vector2 Facing;

        public bool IsFinite =>
            IsFiniteValue(StateStartNetworkTime) &&
            IsFiniteValue(PhaseDuration) && PhaseDuration >= 0f &&
            IsFiniteValue(Facing.x) &&
            IsFiniteValue(Facing.y);

        public bool HasKnownPhase =>
            (byte)Phase <= (byte)EnemyAttackPresentationPhase.Cancelled;

        public double ElapsedAt(double currentNetworkTime)
        {
            if (!IsFiniteValue(currentNetworkTime))
            {
                throw new ArgumentOutOfRangeException(nameof(currentNetworkTime));
            }

            return Math.Max(0d, currentNetworkTime - StateStartNetworkTime);
        }

        public double RemainingAt(double currentNetworkTime)
        {
            return Math.Max(0d, PhaseDuration - ElapsedAt(currentNetworkTime));
        }

        public bool IsExpiredAt(double currentNetworkTime)
        {
            return PhaseDuration > 0f && RemainingAt(currentNetworkTime) <= 0d;
        }

        private static bool IsFiniteValue(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    [Serializable]
    public struct EnemyAttackPresentationBatch
    {
        public uint BatchSequence;
        public EnemyAttackPresentationEdge[] Edges;
    }
}
