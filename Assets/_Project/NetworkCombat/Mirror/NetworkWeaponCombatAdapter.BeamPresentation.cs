using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkWeaponCombatAdapter
    {
        private readonly List<NetworkBeamPresentationEdge> outgoingBeamEdges = new List<NetworkBeamPresentationEdge>();
        private readonly Dictionary<ulong, NetworkBeamPresentationAim> outgoingBeamAims =
            new Dictionary<ulong, NetworkBeamPresentationAim>();
        private readonly BeamPresentationHistory serverBeamHistory = new BeamPresentationHistory();
        private BeamPresentationReplica beamPresentationReplica;
        private uint outgoingBeamSequence, lastServerBeamSequence, lastClientBeamSequence;
        private uint outgoingBeamAimSequence, lastServerBeamAimSequence, lastClientBeamAimSequence;
        private double nextBeamAimSendTime;
        public int SentBeamPresentationCount { get; private set; }
        public int ReceivedBeamPresentationCount { get; private set; }
        public int ReplicaBeamSpawnCount { get; private set; }
        public int ReplicaBeamTerminationCount { get; private set; }
        public int SentBeamAimBatchCount { get; private set; }
        public int ReceivedBeamAimCount { get; private set; }
        public int RejectedBeamPresentationCount { get; private set; }
        public int ReplicaActiveBeamCount => beamPresentationReplica?.ActiveBeamCount ?? 0;

        private void SubscribeBeamPresentations()
        {
            UnsubscribeBeamPresentations();
            if (playerBuildRuntime == null) return;
            playerBuildRuntime.BeamPresentationSpawned += HandleBeamSpawned;
            playerBuildRuntime.BeamPresentationAimChanged += HandleBeamAimChanged;
            playerBuildRuntime.BeamPresentationTerminated += HandleBeamTerminated;
        }

        private void UnsubscribeBeamPresentations()
        {
            if (playerBuildRuntime != null)
            {
                playerBuildRuntime.BeamPresentationSpawned -= HandleBeamSpawned;
                playerBuildRuntime.BeamPresentationAimChanged -= HandleBeamAimChanged;
                playerBuildRuntime.BeamPresentationTerminated -= HandleBeamTerminated;
            }
            outgoingBeamEdges.Clear();
            outgoingBeamAims.Clear();
        }

        private void HandleBeamSpawned(BeamPresentationSpawn spawn) => outgoingBeamEdges.Add(new NetworkBeamPresentationEdge
        {
            SourcePlayerId = netId, WeaponId = spawn.WeaponId, AttackEventId = spawn.Key.AttackEventId,
            BeamIndex = spawn.Key.BeamIndex, BeamCount = spawn.BeamCount, EventNetworkTime = NetworkTime.time,
            Phase = BeamPresentationPhase.Spawn, RootDirection = spawn.RootDirection, Element = spawn.Element,
            AnimationDuration = spawn.AnimationDuration, Stats = spawn.Stats
        });

        private void HandleBeamAimChanged(BeamPresentationAim aim) =>
            outgoingBeamAims[aim.AttackEventId] = new NetworkBeamPresentationAim
            {
                SourcePlayerId = netId, WeaponId = aim.WeaponId, AttackEventId = aim.AttackEventId,
                EventNetworkTime = NetworkTime.time, RootDirection = aim.RootDirection
            };

        private void HandleBeamTerminated(BeamPresentationTermination termination)
        {
            outgoingBeamAims.Remove(termination.Key.AttackEventId);
            outgoingBeamEdges.Add(new NetworkBeamPresentationEdge
            {
                SourcePlayerId = netId, WeaponId = termination.WeaponId, AttackEventId = termination.Key.AttackEventId,
                BeamIndex = termination.Key.BeamIndex, EventNetworkTime = NetworkTime.time,
                Phase = BeamPresentationPhase.Terminated
            });
        }

        private void FlushBeamPresentations()
        {
            for (int offset = 0; offset < outgoingBeamEdges.Count; offset += maximumPresentationEdgesPerBatch)
            {
                int count = Math.Min(maximumPresentationEdgesPerBatch, outgoingBeamEdges.Count - offset);
                var edges = new NetworkBeamPresentationEdge[count];
                outgoingBeamEdges.CopyTo(offset, edges, 0, count);
                outgoingBeamSequence = NextSequence(outgoingBeamSequence);
                CmdSubmitBeamPresentations(new NetworkBeamPresentationBatch { BatchSequence = outgoingBeamSequence, Edges = edges });
                SentBeamPresentationCount += count;
            }
            outgoingBeamEdges.Clear();
        }

        private void FlushBeamAims()
        {
            if (outgoingBeamAims.Count == 0 || NetworkTime.time < nextBeamAimSendTime) return;
            nextBeamAimSendTime = NetworkTime.time + 0.1d;
            var aims = new List<NetworkBeamPresentationAim>(outgoingBeamAims.Values);
            outgoingBeamAims.Clear();
            for (int offset = 0; offset < aims.Count; offset += maximumPresentationEdgesPerBatch)
            {
                int count = Math.Min(maximumPresentationEdgesPerBatch, aims.Count - offset);
                outgoingBeamAimSequence = NextSequence(outgoingBeamAimSequence);
                CmdSubmitBeamAims(new NetworkBeamAimBatch
                {
                    BatchSequence = outgoingBeamAimSequence, Aims = aims.GetRange(offset, count).ToArray()
                });
                SentBeamAimBatchCount++;
            }
        }

        [Command(channel = Channels.Reliable)]
        private void CmdSubmitBeamPresentations(NetworkBeamPresentationBatch batch, NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender != connectionToClient ||
                !IsValidBeamPresentationBatch(batch, netId, lastServerBeamSequence, maximumPresentationEdgesPerBatch))
            { RejectBeamPresentations(batch.Edges?.Length ?? 1); return; }
            lastServerBeamSequence = batch.BatchSequence;
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            var accepted = new List<NetworkBeamPresentationEdge>(batch.Edges.Length);
            foreach (NetworkBeamPresentationEdge edge in batch.Edges)
            {
                bool valid = edge.Phase == BeamPresentationPhase.Spawn
                    ? world != null && world.Gateway.Attacks.Contains(netId, edge.AttackEventId, edge.WeaponId) &&
                      serverBeamHistory.TrySpawn(edge)
                    : serverBeamHistory.TryTerminate(edge);
                if (valid) accepted.Add(edge);
                else RejectBeamPresentations(1);
            }
            if (accepted.Count == 0) return;
            batch.Edges = accepted.ToArray();
            RpcApplyBeamPresentations(batch);
        }

        [Command(channel = Channels.Unreliable)]
        private void CmdSubmitBeamAims(NetworkBeamAimBatch batch, NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender != connectionToClient) return;
            // Dropped/reordered samples and samples preceding a reliable spawn are ordinary
            // transport conditions. The next sample repairs direction; none creates a beam.
            if (!IsValidBeamAimBatch(batch, netId, lastServerBeamAimSequence, maximumPresentationEdgesPerBatch)) return;
            lastServerBeamAimSequence = batch.BatchSequence;
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            var accepted = new List<NetworkBeamPresentationAim>(batch.Aims.Length);
            foreach (NetworkBeamPresentationAim aim in batch.Aims)
                if (world != null && world.Gateway.Attacks.Contains(netId, aim.AttackEventId, aim.WeaponId) &&
                    serverBeamHistory.CanAim(aim.WeaponId, aim.AttackEventId)) accepted.Add(aim);
            if (accepted.Count == 0) return;
            batch.Aims = accepted.ToArray();
            RpcApplyBeamAims(batch);
        }

        [ClientRpc(channel = Channels.Reliable)]
        private void RpcApplyBeamPresentations(NetworkBeamPresentationBatch batch)
        {
            if (!isOwned) ApplyRemoteBeamPresentations(batch, NetworkTime.time);
        }

        private void ApplyRemoteBeamPresentations(NetworkBeamPresentationBatch batch, double now)
        {
            if (!IsValidBeamPresentationBatch(batch, netId, lastClientBeamSequence, maximumPresentationEdgesPerBatch))
            { RejectBeamPresentations(batch.Edges?.Length ?? 1); return; }
            lastClientBeamSequence = batch.BatchSequence;
            RuntimeDB database = playerBootstrap != null ? playerBootstrap.ResolveSharedRuntimeDatabase() : null;
            if (database == null || playerMovement == null) { RejectBeamPresentations(batch.Edges.Length); return; }
            beamPresentationReplica ??= new BeamPresentationReplica(playerMovement, database);
            foreach (NetworkBeamPresentationEdge edge in batch.Edges)
            {
                ReceivedBeamPresentationCount++;
                if (edge.Phase == BeamPresentationPhase.Spawn)
                {
                    double age = Math.Max(0d, now - edge.EventNetworkTime);
                    if (age <= maximumReplayDelay && beamPresentationReplica.TrySpawn(edge.ToSpawn(), (float)age))
                        ReplicaBeamSpawnCount++;
                    else RejectBeamPresentations(1);
                }
                else if (beamPresentationReplica.TryTerminate(edge.ToTermination())) ReplicaBeamTerminationCount++;
            }
        }

        [ClientRpc(channel = Channels.Unreliable)]
        private void RpcApplyBeamAims(NetworkBeamAimBatch batch)
        {
            if (!isOwned) ApplyRemoteBeamAims(batch, NetworkTime.time);
        }

        private void ApplyRemoteBeamAims(NetworkBeamAimBatch batch, double now)
        {
            if (!IsValidBeamAimBatch(batch, netId, lastClientBeamAimSequence, maximumPresentationEdgesPerBatch)) return;
            lastClientBeamAimSequence = batch.BatchSequence;
            if (beamPresentationReplica == null) return;
            foreach (NetworkBeamPresentationAim aim in batch.Aims)
                if (now - aim.EventNetworkTime <= maximumReplayDelay && beamPresentationReplica.TryAim(aim.ToAim()))
                    ReceivedBeamAimCount++;
        }

        public static bool IsValidBeamPresentationBatch(NetworkBeamPresentationBatch batch,
            uint expectedSourcePlayerId, uint previousBatchSequence, int maximumEdges)
        {
            if (expectedSourcePlayerId == 0 || maximumEdges <= 0 || batch.Edges == null || batch.Edges.Length == 0 ||
                batch.Edges.Length > maximumEdges || !ProjectilePresentationSequence.IsNewer(batch.BatchSequence, previousBatchSequence)) return false;
            foreach (NetworkBeamPresentationEdge edge in batch.Edges)
                if (!edge.IsValid || edge.SourcePlayerId != expectedSourcePlayerId) return false;
            return true;
        }

        public static bool IsValidBeamAimBatch(NetworkBeamAimBatch batch,
            uint expectedSourcePlayerId, uint previousBatchSequence, int maximumEdges)
        {
            if (expectedSourcePlayerId == 0 || maximumEdges <= 0 || batch.Aims == null || batch.Aims.Length == 0 ||
                batch.Aims.Length > maximumEdges || !ProjectilePresentationSequence.IsNewer(batch.BatchSequence, previousBatchSequence)) return false;
            foreach (NetworkBeamPresentationAim aim in batch.Aims)
                if (!aim.IsValid || aim.SourcePlayerId != expectedSourcePlayerId) return false;
            return true;
        }

        private void DisposeBeamPresentationReplica()
        {
            beamPresentationReplica?.Dispose();
            beamPresentationReplica = null;
        }
        private void RejectBeamPresentations(int count)
        {
            RejectedBeamPresentationCount += count;
            RejectedPresentationCount += count;
        }
    }
}
