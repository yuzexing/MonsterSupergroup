using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkWeaponCombatAdapter
    {
        private readonly List<NetworkTrailPresentationEdge> outgoingTrailEdges = new List<NetworkTrailPresentationEdge>();
        private readonly TrailPresentationHistory serverTrailHistory = new TrailPresentationHistory();
        private readonly Dictionary<ulong, ulong> serverDashWeaponUses = new Dictionary<ulong, ulong>();
        private TrailPresentationReplica trailPresentationReplica;
        private uint outgoingTrailSequence, lastServerTrailSequence, lastClientTrailSequence;
        public int SentTrailPresentationCount { get; private set; }
        public int ReceivedTrailPresentationCount { get; private set; }
        public int ReplicaTrailSpawnCount { get; private set; }
        public int ReplicaTrailPointCount { get; private set; }
        public int ReplicaTrailSamplingEndCount { get; private set; }
        public int ReplicaTrailTerminationCount { get; private set; }
        public int RejectedTrailPresentationCount { get; private set; }
        public int ReplicaActiveTrailCount => trailPresentationReplica?.ActiveTrailCount ?? 0;

        private void SubscribeTrailPresentations()
        {
            UnsubscribeTrailPresentations();
            if (playerBuildRuntime == null) return;
            playerBuildRuntime.TrailPresentationSpawned += HandleTrailSpawned;
            playerBuildRuntime.TrailPresentationPointAdded += HandleTrailPoint;
            playerBuildRuntime.TrailPresentationSamplingEnded += HandleTrailSamplingEnded;
            playerBuildRuntime.TrailPresentationTerminated += HandleTrailTerminated;
        }
        private void UnsubscribeTrailPresentations()
        {
            if (playerBuildRuntime != null)
            {
                playerBuildRuntime.TrailPresentationSpawned -= HandleTrailSpawned;
                playerBuildRuntime.TrailPresentationPointAdded -= HandleTrailPoint;
                playerBuildRuntime.TrailPresentationSamplingEnded -= HandleTrailSamplingEnded;
                playerBuildRuntime.TrailPresentationTerminated -= HandleTrailTerminated;
            }
            outgoingTrailEdges.Clear();
        }
        private void HandleTrailSpawned(TrailPresentationSpawn spawn) => outgoingTrailEdges.Add(new NetworkTrailPresentationEdge
        {
            SourcePlayerId = netId, WeaponId = spawn.WeaponId, AttackEventId = spawn.AttackEventId,
            DashUseId = spawn.DashUseId, Phase = TrailPresentationPhase.Spawn, EventNetworkTime = NetworkTime.time,
            Element = spawn.Element, Position = spawn.Origin, SamplingDuration = spawn.SamplingDuration,
            SegmentDuration = spawn.SegmentDuration, HitInterval = spawn.HitInterval, Stats = spawn.Stats
        });
        private void HandleTrailPoint(TrailPresentationPoint point) => outgoingTrailEdges.Add(new NetworkTrailPresentationEdge
        {
            SourcePlayerId = netId, WeaponId = point.WeaponId, AttackEventId = point.AttackEventId,
            Phase = TrailPresentationPhase.Point, EventNetworkTime = NetworkTime.time,
            PointIndex = point.PointIndex, Position = point.WorldPosition, ElapsedSeconds = point.ElapsedSeconds
        });
        private void HandleTrailSamplingEnded(TrailPresentationSamplingEnded end) => outgoingTrailEdges.Add(new NetworkTrailPresentationEdge
        {
            SourcePlayerId = netId, WeaponId = end.WeaponId, AttackEventId = end.AttackEventId,
            Phase = TrailPresentationPhase.SamplingEnded, EventNetworkTime = NetworkTime.time, ElapsedSeconds = end.SamplingElapsedSeconds
        });
        private void HandleTrailTerminated(TrailPresentationTermination end) => outgoingTrailEdges.Add(new NetworkTrailPresentationEdge
        {
            SourcePlayerId = netId, WeaponId = end.WeaponId, AttackEventId = end.AttackEventId,
            Phase = TrailPresentationPhase.Terminated, EventNetworkTime = NetworkTime.time
        });
        private void FlushTrailPresentations()
        {
            for (int offset = 0; offset < outgoingTrailEdges.Count; offset += maximumPresentationEdgesPerBatch)
            {
                int count = Math.Min(maximumPresentationEdgesPerBatch, outgoingTrailEdges.Count - offset);
                var edges = new NetworkTrailPresentationEdge[count];
                outgoingTrailEdges.CopyTo(offset, edges, 0, count);
                outgoingTrailSequence = NextSequence(outgoingTrailSequence);
                CmdSubmitTrailPresentations(new NetworkTrailPresentationBatch { BatchSequence = outgoingTrailSequence, Edges = edges });
                SentTrailPresentationCount += count;
            }
            outgoingTrailEdges.Clear();
        }
        [Command(channel = Channels.Reliable)]
        private void CmdSubmitTrailPresentations(NetworkTrailPresentationBatch batch, NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender != connectionToClient ||
                !IsValidTrailPresentationBatch(batch, netId, lastServerTrailSequence, maximumPresentationEdgesPerBatch))
            { RejectTrailPresentations(batch.Edges?.Length ?? 1); return; }
            lastServerTrailSequence = batch.BatchSequence;
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            var accepted = new List<NetworkTrailPresentationEdge>(batch.Edges.Length);
            foreach (NetworkTrailPresentationEdge edge in batch.Edges)
            {
                bool admitted = edge.Phase != TrailPresentationPhase.Spawn ||
                    (world != null && world.Gateway.Attacks.Contains(netId, edge.AttackEventId, edge.WeaponId) &&
                     serverDashWeaponUses.TryGetValue(edge.AttackEventId, out ulong useId) && useId == edge.DashUseId);
                if (admitted && serverTrailHistory.TryApply(edge)) accepted.Add(edge);
                else RejectTrailPresentations(1);
            }
            if (accepted.Count == 0) return;
            batch.Edges = accepted.ToArray();
            RpcApplyTrailPresentations(batch);
        }
        [ClientRpc(channel = Channels.Reliable)]
        private void RpcApplyTrailPresentations(NetworkTrailPresentationBatch batch)
        {
            if (!isOwned) ApplyRemoteTrailPresentations(batch, NetworkTime.time);
        }
        private void ApplyRemoteTrailPresentations(NetworkTrailPresentationBatch batch, double now)
        {
            if (!IsValidTrailPresentationBatch(batch, netId, lastClientTrailSequence, maximumPresentationEdgesPerBatch))
            { RejectTrailPresentations(batch.Edges?.Length ?? 1); return; }
            lastClientTrailSequence = batch.BatchSequence;
            RuntimeDB database = playerBootstrap != null ? playerBootstrap.ResolveSharedRuntimeDatabase() : null;
            if (database == null || playerMovement == null) { RejectTrailPresentations(batch.Edges.Length); return; }
            trailPresentationReplica ??= new TrailPresentationReplica(playerMovement, database);
            foreach (NetworkTrailPresentationEdge edge in batch.Edges)
            {
                ReceivedTrailPresentationCount++;
                float age = (float)Math.Max(0d, now - edge.EventNetworkTime);
                if (edge.Phase == TrailPresentationPhase.Spawn)
                {
                    if (age <= maximumReplayDelay && trailPresentationReplica.TrySpawn(edge.ToSpawn(), age)) ReplicaTrailSpawnCount++;
                    else RejectTrailPresentations(1);
                }
                else if (edge.Phase == TrailPresentationPhase.Point)
                {
                    if (trailPresentationReplica.TryPoint(edge.ToPoint(), age)) ReplicaTrailPointCount++;
                }
                else if (edge.Phase == TrailPresentationPhase.SamplingEnded)
                {
                    if (trailPresentationReplica.TryEndSampling(edge.ToSamplingEnded(), age)) ReplicaTrailSamplingEndCount++;
                }
                else if (trailPresentationReplica.TryTerminate(edge.ToTermination())) ReplicaTrailTerminationCount++;
            }
        }
        public static bool IsValidTrailPresentationBatch(NetworkTrailPresentationBatch batch,
            uint source, uint previousSequence, int maximumEdges)
        {
            if (source == 0 || maximumEdges <= 0 || batch.Edges == null || batch.Edges.Length == 0 ||
                batch.Edges.Length > maximumEdges || !ProjectilePresentationSequence.IsNewer(batch.BatchSequence, previousSequence)) return false;
            foreach (NetworkTrailPresentationEdge edge in batch.Edges)
                if (!edge.IsValid || edge.SourcePlayerId != source) return false;
            return true;
        }
        private void DisposeTrailPresentationReplica()
        { trailPresentationReplica?.Dispose(); trailPresentationReplica = null; }
        private void RejectTrailPresentations(int count)
        { RejectedTrailPresentationCount += count; RejectedPresentationCount += count; }
    }
}
