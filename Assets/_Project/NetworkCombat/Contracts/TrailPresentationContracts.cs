using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public enum TrailPresentationPhase : byte { Spawn, Point, SamplingEnded, Terminated }
    [Serializable]
    public struct NetworkTrailPresentationEdge
    {
        public uint SourcePlayerId, WeaponId;
        public ulong AttackEventId, DashUseId;
        public double EventNetworkTime;
        public TrailPresentationPhase Phase;
        public AttackElement Element;
        public Vector2 Position;
        public float SamplingDuration, SegmentDuration, HitInterval, ElapsedSeconds;
        public uint PointIndex;
        public ProjectilePresentationStats Stats;

        public bool IsValid => SourcePlayerId != 0 && WeaponId != 0 && AttackEventId != 0 &&
            Finite(EventNetworkTime) && EventNetworkTime >= 0 && (byte)Phase <= (byte)TrailPresentationPhase.Terminated &&
            (Phase == TrailPresentationPhase.Terminated ||
             (Phase == TrailPresentationPhase.Spawn ? ToSpawn().IsValid :
              Finite(ElapsedSeconds) && ElapsedSeconds >= 0 &&
              (Phase != TrailPresentationPhase.Point || (Finite(Position.x) && Finite(Position.y)))));

        public TrailPresentationSpawn ToSpawn() => new TrailPresentationSpawn(WeaponId, AttackEventId, DashUseId,
            Element, Position, SamplingDuration, SegmentDuration, HitInterval, Stats);
        public TrailPresentationPoint ToPoint() => new TrailPresentationPoint(WeaponId, AttackEventId, PointIndex, Position, ElapsedSeconds);
        public TrailPresentationSamplingEnded ToSamplingEnded() => new TrailPresentationSamplingEnded(WeaponId, AttackEventId, ElapsedSeconds);
        public TrailPresentationTermination ToTermination() => new TrailPresentationTermination(WeaponId, AttackEventId);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    [Serializable]
    public struct NetworkTrailPresentationBatch
    {
        public uint BatchSequence;
        public NetworkTrailPresentationEdge[] Edges;
    }

    /// <summary>Reliable source point order and visual lifetime; no collision or damage simulation.</summary>
    public sealed class TrailPresentationHistory
    {
        private readonly Dictionary<ulong, Root> roots = new Dictionary<ulong, Root>();
        public int AttackCount => roots.Count;
        public bool TryApply(NetworkTrailPresentationEdge edge)
        {
            if (!edge.IsValid) return false;
            if (edge.Phase == TrailPresentationPhase.Spawn)
            {
                if (roots.ContainsKey(edge.AttackEventId)) return false;
                roots.Add(edge.AttackEventId, new Root { First = edge });
                return true;
            }
            if (!roots.TryGetValue(edge.AttackEventId, out Root root) || root.Terminated ||
                root.First.WeaponId != edge.WeaponId || root.First.SourcePlayerId != edge.SourcePlayerId ||
                edge.EventNetworkTime < root.First.EventNetworkTime) return false;
            if (edge.Phase == TrailPresentationPhase.Terminated) { root.Terminated = true; return true; }
            if (root.SamplingEnded || edge.ElapsedSeconds < root.LastElapsed) return false;
            if (edge.Phase == TrailPresentationPhase.Point)
            {
                if (edge.PointIndex != root.NextPoint || root.NextPoint == uint.MaxValue ||
                    edge.ElapsedSeconds >= root.First.SamplingDuration) return false;
                root.NextPoint++;
            }
            else
            {
                if (edge.ElapsedSeconds < root.First.SamplingDuration) return false;
                root.SamplingEnded = true;
            }
            root.LastElapsed = edge.ElapsedSeconds;
            return true;
        }
        public void RetireAttack(ulong root) => roots.Remove(root);
        public void Clear() => roots.Clear();
        private sealed class Root
        {
            public NetworkTrailPresentationEdge First;
            public uint NextPoint;
            public float LastElapsed;
            public bool SamplingEnded, Terminated;
        }
    }
}
