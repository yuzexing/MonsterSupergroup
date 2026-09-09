using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public enum MeleePresentationPhase : byte
    {
        Spawn = 0,
        Terminated = 1
    }

    [Serializable]
    public struct NetworkMeleePresentationEdge
    {
        public uint SourcePlayerId;
        public uint WeaponId;
        public ulong AttackEventId;
        public ushort SlashIndex;
        public double EventNetworkTime;
        public MeleePresentationPhase Phase;
        public Vector3 LocalPosition;
        public Vector2 Direction;
        public float AnimationDuration;
        public ProjectilePresentationStats Stats;

        public MeleePresentationKey Key => new MeleePresentationKey(AttackEventId, SlashIndex);

        public bool IsValid
        {
            get
            {
                if (SourcePlayerId == 0u || WeaponId == 0u || !Key.IsValid ||
                    (byte)Phase > (byte)MeleePresentationPhase.Terminated ||
                    !IsFinite(EventNetworkTime)) return false;

                // A termination identifies an existing visual; it carries no gameplay stats.
                if (Phase == MeleePresentationPhase.Terminated) return true;

                return IsFinite(LocalPosition.x) && IsFinite(LocalPosition.y) &&
                    IsFinite(LocalPosition.z) && IsFinite(Direction.x) &&
                    IsFinite(Direction.y) && IsFinite(Direction.sqrMagnitude) &&
                    Direction.sqrMagnitude > 0.000001f &&
                    IsFinite(AnimationDuration) &&
                    (AnimationDuration == -1f || AnimationDuration > 0f) && Stats.IsFinite;
            }
        }

        public MeleePresentationSpawn ToSpawn() => new MeleePresentationSpawn(
            WeaponId, Key, LocalPosition, Direction, AnimationDuration, Stats);

        public MeleePresentationTermination ToTermination() =>
            new MeleePresentationTermination(WeaponId, Key);

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }

    [Serializable]
    public struct NetworkMeleePresentationBatch
    {
        public uint BatchSequence;
        public NetworkMeleePresentationEdge[] Edges;
    }

    /// <summary>
    /// Server forwarding history for admitted roots, independent of a visual's duration.
    /// RetireAttack must follow the existing attack admission lifetime.
    /// </summary>
    public sealed class MeleePresentationHistory
    {
        private readonly Dictionary<ulong, HashSet<ushort>> slashesByRoot =
            new Dictionary<ulong, HashSet<ushort>>();

        public int AttackCount => slashesByRoot.Count;

        public bool TryRecord(MeleePresentationKey key)
        {
            if (!key.IsValid) return false;
            if (!slashesByRoot.TryGetValue(key.AttackEventId, out HashSet<ushort> slashes))
            {
                slashes = new HashSet<ushort>();
                slashesByRoot.Add(key.AttackEventId, slashes);
            }
            return slashes.Add(key.SlashIndex);
        }

        public void RetireAttack(ulong attackEventId) => slashesByRoot.Remove(attackEventId);
        public void Clear() => slashesByRoot.Clear();
    }
}
