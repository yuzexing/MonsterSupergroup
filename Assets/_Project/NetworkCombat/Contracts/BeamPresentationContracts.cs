using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public enum BeamPresentationPhase : byte { Spawn, Terminated }

    [Serializable]
    public struct NetworkBeamPresentationEdge
    {
        public uint SourcePlayerId;
        public uint WeaponId;
        public ulong AttackEventId;
        public ushort BeamIndex;
        public int BeamCount;
        public double EventNetworkTime;
        public BeamPresentationPhase Phase;
        public Vector2 RootDirection;
        public AttackElement Element;
        public float AnimationDuration;
        public ProjectilePresentationStats Stats;
        public BeamPresentationKey Key => new BeamPresentationKey(AttackEventId, BeamIndex);

        public bool IsValid => SourcePlayerId != 0 && WeaponId != 0 && Key.IsValid &&
            IsFinite(EventNetworkTime) && (byte)Phase <= (byte)BeamPresentationPhase.Terminated &&
            (Phase == BeamPresentationPhase.Terminated ||
             (BeamCount > 0 && BeamCount <= ushort.MaxValue + 1 && BeamIndex < BeamCount &&
              IsDirection(RootDirection) && (int)Element >= 0 && (int)Element <= (int)AttackElement.Fire &&
              IsFinite(AnimationDuration) && AnimationDuration >= 0 && Stats.IsFinite));

        public BeamPresentationSpawn ToSpawn() => new BeamPresentationSpawn(
            WeaponId, Key, BeamCount, RootDirection, Element, AnimationDuration, Stats);
        public BeamPresentationTermination ToTermination() => new BeamPresentationTermination(WeaponId, Key);
        internal static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal static bool IsDirection(Vector2 value) => IsFinite(value.x) && IsFinite(value.y) &&
            IsFinite(value.sqrMagnitude) && value.sqrMagnitude > 0.000001f;
    }

    [Serializable]
    public struct NetworkBeamPresentationBatch
    {
        public uint BatchSequence;
        public NetworkBeamPresentationEdge[] Edges;
    }

    [Serializable]
    public struct NetworkBeamPresentationAim
    {
        public uint SourcePlayerId;
        public uint WeaponId;
        public ulong AttackEventId;
        public double EventNetworkTime;
        public Vector2 RootDirection;
        public bool IsValid => SourcePlayerId != 0 && WeaponId != 0 && AttackEventId != 0 &&
            NetworkBeamPresentationEdge.IsFinite(EventNetworkTime) &&
            NetworkBeamPresentationEdge.IsDirection(RootDirection);
        public BeamPresentationAim ToAim() => new BeamPresentationAim(WeaponId, AttackEventId, RootDirection);
    }

    [Serializable]
    public struct NetworkBeamAimBatch
    {
        public uint BatchSequence;
        public NetworkBeamPresentationAim[] Aims;
    }

    /// <summary>Visual forwarding history. Gameplay admission and damage stay in their existing runtimes.</summary>
    public sealed class BeamPresentationHistory
    {
        private sealed class Root
        {
            public uint WeaponId;
            public int Count;
            public AttackElement Element;
            public readonly HashSet<ushort> Seen = new HashSet<ushort>();
            public readonly HashSet<ushort> Active = new HashSet<ushort>();
        }
        private readonly Dictionary<ulong, Root> roots = new Dictionary<ulong, Root>();
        public int AttackCount => roots.Count;

        public bool TrySpawn(NetworkBeamPresentationEdge edge)
        {
            if (!edge.IsValid || edge.Phase != BeamPresentationPhase.Spawn) return false;
            if (!roots.TryGetValue(edge.AttackEventId, out Root root))
            {
                root = new Root { WeaponId = edge.WeaponId, Count = edge.BeamCount, Element = edge.Element };
                roots.Add(edge.AttackEventId, root);
            }
            if (root.WeaponId != edge.WeaponId || root.Count != edge.BeamCount || root.Element != edge.Element ||
                !root.Seen.Add(edge.BeamIndex)) return false;
            root.Active.Add(edge.BeamIndex);
            return true;
        }

        public bool TryTerminate(NetworkBeamPresentationEdge edge) => edge.IsValid &&
            edge.Phase == BeamPresentationPhase.Terminated &&
            roots.TryGetValue(edge.AttackEventId, out Root root) && root.WeaponId == edge.WeaponId &&
            root.Active.Remove(edge.BeamIndex);

        public bool CanAim(uint weaponId, ulong attackEventId) =>
            roots.TryGetValue(attackEventId, out Root root) && root.WeaponId == weaponId && root.Active.Count > 0;
        public void RetireAttack(ulong attackEventId) => roots.Remove(attackEventId);
        public void Clear() => roots.Clear();
    }
}
