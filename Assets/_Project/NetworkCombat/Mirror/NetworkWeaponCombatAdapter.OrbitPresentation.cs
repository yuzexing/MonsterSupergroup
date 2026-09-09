using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkWeaponCombatAdapter
    {
        private readonly List<NetworkOrbitPresentationEdge> outgoingOrbitEdges = new List<NetworkOrbitPresentationEdge>();
        private readonly OrbitPresentationHistory serverOrbitHistory = new OrbitPresentationHistory();
        private OrbitPresentationReplica orbitPresentationReplica;
        private uint outgoingOrbitSequence, lastServerOrbitSequence, lastClientOrbitSequence;
        public int SentOrbitPresentationCount { get; private set; }
        public int ReceivedOrbitPresentationCount { get; private set; }
        public int ReplicaOrbSpawnCount { get; private set; }
        public int ReplicaOrbHidingCount { get; private set; }
        public int ReplicaOrbTerminationCount { get; private set; }
        public int RejectedOrbitPresentationCount { get; private set; }
        public int ReplicaActiveOrbCount => orbitPresentationReplica?.ActiveOrbCount ?? 0;

        private void SubscribeOrbitPresentations()
        {
            UnsubscribeOrbitPresentations();
            if (playerBuildRuntime == null) return;
            playerBuildRuntime.OrbitPresentationSpawned += HandleOrbitSpawned;
            playerBuildRuntime.OrbitPresentationHiding += HandleOrbitHiding;
            playerBuildRuntime.OrbitPresentationTerminated += HandleOrbitTerminated;
        }

        private void UnsubscribeOrbitPresentations()
        {
            if (playerBuildRuntime != null)
            {
                playerBuildRuntime.OrbitPresentationSpawned -= HandleOrbitSpawned;
                playerBuildRuntime.OrbitPresentationHiding -= HandleOrbitHiding;
                playerBuildRuntime.OrbitPresentationTerminated -= HandleOrbitTerminated;
            }
            outgoingOrbitEdges.Clear();
        }

        private void HandleOrbitSpawned(OrbitPresentationSpawn spawn) => outgoingOrbitEdges.Add(new NetworkOrbitPresentationEdge
        {
            SourcePlayerId = netId, WeaponId = spawn.WeaponId, AttackEventId = spawn.Key.AttackEventId,
            OrbIndex = spawn.Key.OrbIndex, OrbCount = spawn.OrbCount, EventNetworkTime = NetworkTime.time,
            Phase = OrbitPresentationPhase.Spawn, InitialPhaseRadians = spawn.InitialPhaseRadians,
            Radius = spawn.Radius, AngularSpeedRadians = spawn.AngularSpeedRadians,
            OrbitDuration = spawn.OrbitDuration, Stats = spawn.Stats
        });

        private void HandleOrbitHiding(OrbitPresentationHiding hiding) => outgoingOrbitEdges.Add(new NetworkOrbitPresentationEdge
        {
            SourcePlayerId = netId, WeaponId = hiding.WeaponId, AttackEventId = hiding.Key.AttackEventId,
            OrbIndex = hiding.Key.OrbIndex, EventNetworkTime = NetworkTime.time,
            Phase = OrbitPresentationPhase.Hiding, OrbitElapsedSeconds = hiding.OrbitElapsedSeconds
        });

        private void HandleOrbitTerminated(OrbitPresentationTermination termination) => outgoingOrbitEdges.Add(new NetworkOrbitPresentationEdge
        {
            SourcePlayerId = netId, WeaponId = termination.WeaponId, AttackEventId = termination.Key.AttackEventId,
            OrbIndex = termination.Key.OrbIndex, EventNetworkTime = NetworkTime.time,
            Phase = OrbitPresentationPhase.Terminated
        });

        private void FlushOrbitPresentations()
        {
            for (int offset = 0; offset < outgoingOrbitEdges.Count; offset += maximumPresentationEdgesPerBatch)
            {
                int count = Math.Min(maximumPresentationEdgesPerBatch, outgoingOrbitEdges.Count - offset);
                var edges = new NetworkOrbitPresentationEdge[count];
                outgoingOrbitEdges.CopyTo(offset, edges, 0, count);
                outgoingOrbitSequence = NextSequence(outgoingOrbitSequence);
                CmdSubmitOrbitPresentations(new NetworkOrbitPresentationBatch { BatchSequence = outgoingOrbitSequence, Edges = edges });
                SentOrbitPresentationCount += count;
            }
            outgoingOrbitEdges.Clear();
        }

        [Command(channel = Channels.Reliable)]
        private void CmdSubmitOrbitPresentations(NetworkOrbitPresentationBatch batch, NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender != connectionToClient ||
                !IsValidOrbitPresentationBatch(batch, netId, lastServerOrbitSequence, maximumPresentationEdgesPerBatch))
            { RejectOrbitPresentations(batch.Edges?.Length ?? 1); return; }
            lastServerOrbitSequence = batch.BatchSequence;
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            var accepted = new List<NetworkOrbitPresentationEdge>(batch.Edges.Length);
            foreach (NetworkOrbitPresentationEdge edge in batch.Edges)
            {
                bool valid;
                if (edge.Phase == OrbitPresentationPhase.Spawn)
                    valid = world != null && world.Gateway.Attacks.Contains(netId, edge.AttackEventId, edge.WeaponId) &&
                        serverOrbitHistory.TrySpawn(edge);
                else if (edge.Phase == OrbitPresentationPhase.Hiding) valid = serverOrbitHistory.TryHide(edge);
                else valid = serverOrbitHistory.TryTerminate(edge);
                if (valid) accepted.Add(edge);
                else RejectOrbitPresentations(1);
            }
            if (accepted.Count == 0) return;
            batch.Edges = accepted.ToArray();
            RpcApplyOrbitPresentations(batch);
        }

        [ClientRpc(channel = Channels.Reliable)]
        private void RpcApplyOrbitPresentations(NetworkOrbitPresentationBatch batch)
        {
            if (!isOwned) ApplyRemoteOrbitPresentations(batch, NetworkTime.time);
        }

        private void ApplyRemoteOrbitPresentations(NetworkOrbitPresentationBatch batch, double now)
        {
            if (!IsValidOrbitPresentationBatch(batch, netId, lastClientOrbitSequence, maximumPresentationEdgesPerBatch))
            { RejectOrbitPresentations(batch.Edges?.Length ?? 1); return; }
            lastClientOrbitSequence = batch.BatchSequence;
            RuntimeDB database = playerBootstrap != null ? playerBootstrap.ResolveSharedRuntimeDatabase() : null;
            if (database == null || playerMovement == null) { RejectOrbitPresentations(batch.Edges.Length); return; }
            orbitPresentationReplica ??= new OrbitPresentationReplica(playerMovement, database);
            foreach (NetworkOrbitPresentationEdge edge in batch.Edges)
            {
                ReceivedOrbitPresentationCount++;
                double age = Math.Max(0d, now - edge.EventNetworkTime);
                if (edge.Phase == OrbitPresentationPhase.Spawn)
                {
                    if (age <= maximumReplayDelay && orbitPresentationReplica.TrySpawn(edge.ToSpawn(), (float)age))
                        ReplicaOrbSpawnCount++;
                    else RejectOrbitPresentations(1);
                }
                else if (edge.Phase == OrbitPresentationPhase.Hiding)
                {
                    if (orbitPresentationReplica.TryHide(edge.ToHiding(), (float)age)) ReplicaOrbHidingCount++;
                }
                else if (orbitPresentationReplica.TryTerminate(edge.ToTermination())) ReplicaOrbTerminationCount++;
            }
        }

        public static bool IsValidOrbitPresentationBatch(NetworkOrbitPresentationBatch batch,
            uint expectedSourcePlayerId, uint previousBatchSequence, int maximumEdges)
        {
            if (expectedSourcePlayerId == 0 || maximumEdges <= 0 || batch.Edges == null || batch.Edges.Length == 0 ||
                batch.Edges.Length > maximumEdges || !ProjectilePresentationSequence.IsNewer(batch.BatchSequence, previousBatchSequence)) return false;
            foreach (NetworkOrbitPresentationEdge edge in batch.Edges)
                if (!edge.IsValid || edge.SourcePlayerId != expectedSourcePlayerId) return false;
            return true;
        }

        private void DisposeOrbitPresentationReplica()
        {
            orbitPresentationReplica?.Dispose();
            orbitPresentationReplica = null;
        }
        private void RejectOrbitPresentations(int count)
        {
            RejectedOrbitPresentationCount += count;
            RejectedPresentationCount += count;
        }
    }
}
