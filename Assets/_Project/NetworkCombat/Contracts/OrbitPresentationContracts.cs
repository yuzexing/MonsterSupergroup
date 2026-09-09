using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Player.Attacks;

namespace MonsterSupergroup.NetworkCombat
{
    public enum OrbitPresentationPhase : byte { Spawn, Hiding, Terminated }

    [Serializable]
    public struct NetworkOrbitPresentationEdge
    {
        public uint SourcePlayerId;
        public uint WeaponId;
        public ulong AttackEventId;
        public ushort OrbIndex;
        public int OrbCount;
        public double EventNetworkTime;
        public OrbitPresentationPhase Phase;
        public float InitialPhaseRadians;
        public float Radius;
        public float AngularSpeedRadians;
        public float OrbitDuration;
        public float OrbitElapsedSeconds;
        public ProjectilePresentationStats Stats;
        public OrbitPresentationKey Key => new OrbitPresentationKey(AttackEventId, OrbIndex);

        public bool IsValid => SourcePlayerId != 0 && WeaponId != 0 && Key.IsValid &&
            IsFinite(EventNetworkTime) && (byte)Phase <= (byte)OrbitPresentationPhase.Terminated &&
            (Phase == OrbitPresentationPhase.Terminated ||
             (Phase == OrbitPresentationPhase.Hiding
                ? IsFinite(OrbitElapsedSeconds) && OrbitElapsedSeconds >= 0
                : OrbCount > 0 && OrbCount <= ushort.MaxValue + 1 && OrbIndex < OrbCount &&
                  IsFinite(InitialPhaseRadians) && IsFinite(Radius) && Radius >= 0 &&
                  IsFinite(AngularSpeedRadians) && IsFinite(OrbitDuration) && OrbitDuration >= 0 &&
                  OrbitPresentationSpawn.IsFinitePhase(InitialPhaseRadians, AngularSpeedRadians, OrbitDuration) && Stats.IsFinite));

        public OrbitPresentationSpawn ToSpawn() => new OrbitPresentationSpawn(WeaponId, Key, OrbCount,
            InitialPhaseRadians, Radius, AngularSpeedRadians, OrbitDuration, Stats);
        public OrbitPresentationHiding ToHiding() => new OrbitPresentationHiding(WeaponId, Key, OrbitElapsedSeconds);
        public OrbitPresentationTermination ToTermination() => new OrbitPresentationTermination(WeaponId, Key);
        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    [Serializable]
    public struct NetworkOrbitPresentationBatch
    {
        public uint BatchSequence;
        public NetworkOrbitPresentationEdge[] Edges;
    }

    /// <summary>Per-root visual deduplication; no gameplay runtime is created here.</summary>
    public sealed class OrbitPresentationHistory
    {
        private sealed class Root
        {
            public NetworkOrbitPresentationEdge First;
            public readonly Dictionary<ushort, float> InitialPhases = new Dictionary<ushort, float>();
            public readonly HashSet<ushort> Active = new HashSet<ushort>();
            public readonly HashSet<ushort> Hiding = new HashSet<ushort>();
        }
        private readonly Dictionary<ulong, Root> roots = new Dictionary<ulong, Root>();
        public int AttackCount => roots.Count;

        public bool TrySpawn(NetworkOrbitPresentationEdge edge)
        {
            if (!edge.IsValid || edge.Phase != OrbitPresentationPhase.Spawn) return false;
            if (!roots.TryGetValue(edge.AttackEventId, out Root root))
            {
                root = new Root { First = edge };
                roots.Add(edge.AttackEventId, root);
            }
            NetworkOrbitPresentationEdge first = root.First;
            if (first.WeaponId != edge.WeaponId || first.OrbCount != edge.OrbCount ||
                first.Radius != edge.Radius || first.AngularSpeedRadians != edge.AngularSpeedRadians ||
                first.OrbitDuration != edge.OrbitDuration || !first.Stats.Equals(edge.Stats) ||
                root.InitialPhases.ContainsKey(edge.OrbIndex)) return false;
            root.InitialPhases.Add(edge.OrbIndex, edge.InitialPhaseRadians);
            root.Active.Add(edge.OrbIndex);
            return true;
        }

        public bool TryHide(NetworkOrbitPresentationEdge edge)
        {
            if (!edge.IsValid || edge.Phase != OrbitPresentationPhase.Hiding ||
                !roots.TryGetValue(edge.AttackEventId, out Root root) || root.First.WeaponId != edge.WeaponId ||
                !root.Active.Contains(edge.OrbIndex) || edge.OrbitElapsedSeconds < root.First.OrbitDuration)
                return false;
            return OrbitPresentationSpawn.IsFinitePhase(root.InitialPhases[edge.OrbIndex],
                root.First.AngularSpeedRadians, edge.OrbitElapsedSeconds) && root.Hiding.Add(edge.OrbIndex);
        }

        public bool TryTerminate(NetworkOrbitPresentationEdge edge) => edge.IsValid &&
            edge.Phase == OrbitPresentationPhase.Terminated &&
            roots.TryGetValue(edge.AttackEventId, out Root root) && root.First.WeaponId == edge.WeaponId &&
            root.Active.Remove(edge.OrbIndex);

        public void RetireAttack(ulong attackEventId) => roots.Remove(attackEventId);
        public void Clear() => roots.Clear();
    }
}
