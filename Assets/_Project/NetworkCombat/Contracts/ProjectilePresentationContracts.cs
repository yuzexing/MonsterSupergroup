using System;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public static class ProjectilePresentationSequence
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

    [Serializable]
    public struct NetworkProjectilePresentationEdge
    {
        public uint SourcePlayerId;
        public uint WeaponId;
        public ulong AttackEventId;
        public ushort ProjectileIndex;
        public double EventNetworkTime;
        public ProjectilePresentationPhase Phase;
        public Vector3 Position;
        public Vector2 Direction;
        public AttackElement Element;
        public bool RotateToMovement;
        public ProjectilePresentationStats Stats;

        public ProjectilePresentationKey Key =>
            new ProjectilePresentationKey(AttackEventId, ProjectileIndex);

        public bool HasKnownPhase =>
            (byte)Phase <= (byte)ProjectilePresentationPhase.Cancelled;

        public bool IsValid
        {
            get
            {
                if (SourcePlayerId == 0u || WeaponId == 0u ||
                    AttackEventId == 0UL || !HasKnownPhase ||
                    !IsFiniteValue(EventNetworkTime) || !IsFinite(Position))
                {
                    return false;
                }

                if (Phase != ProjectilePresentationPhase.Spawn)
                {
                    return true;
                }

                return IsFinite(Direction) &&
                    Direction.sqrMagnitude > 0.000001f &&
                    (byte)Element <= (byte)AttackElement.Fire &&
                    Stats.IsFinite;
            }
        }

        public ProjectilePresentationSpawn ToSpawn()
        {
            return new ProjectilePresentationSpawn(
                WeaponId,
                Key,
                Position,
                Direction,
                Element,
                RotateToMovement,
                Stats);
        }

        public ProjectilePresentationTermination ToTermination()
        {
            return new ProjectilePresentationTermination(
                WeaponId,
                Key,
                Position,
                Phase);
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFiniteValue(value.x) && IsFiniteValue(value.y);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFiniteValue(value.x) && IsFiniteValue(value.y) &&
                IsFiniteValue(value.z);
        }

        private static bool IsFiniteValue(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    [Serializable]
    public struct NetworkProjectilePresentationBatch
    {
        public uint BatchSequence;
        public NetworkProjectilePresentationEdge[] Edges;
    }
}
