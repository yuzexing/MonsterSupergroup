using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkWeaponCombatAdapter
    {
        private readonly List<NetworkMeleePresentationEdge> outgoingMeleePresentations =
            new List<NetworkMeleePresentationEdge>(32);
        private MeleePresentationReplica meleePresentationReplica;
        private uint outgoingMeleeBatchSequence;
        private uint lastServerMeleeBatchSequence;
        private uint lastClientMeleeBatchSequence;
        private readonly MeleePresentationHistory serverMeleePresentationHistory =
            new MeleePresentationHistory();

        public int SentMeleePresentationCount { get; private set; }
        public int ReceivedMeleePresentationCount { get; private set; }
        public int ReplicaMeleeSpawnCount { get; private set; }
        public int ReplicaMeleeTerminationCount { get; private set; }
        public int RejectedMeleePresentationCount { get; private set; }
        public int ReplicaActiveMeleeCount => meleePresentationReplica?.ActiveSlashCount ?? 0;

        private void SubscribeMeleePresentations()
        {
            if (playerBuildRuntime == null) return;
            playerBuildRuntime.MeleePresentationSpawned -= HandleMeleePresentationSpawned;
            playerBuildRuntime.MeleePresentationTerminated -= HandleMeleePresentationTerminated;
            playerBuildRuntime.MeleePresentationSpawned += HandleMeleePresentationSpawned;
            playerBuildRuntime.MeleePresentationTerminated += HandleMeleePresentationTerminated;
        }

        private void UnsubscribeMeleePresentations()
        {
            if (playerBuildRuntime != null)
            {
                playerBuildRuntime.MeleePresentationSpawned -= HandleMeleePresentationSpawned;
                playerBuildRuntime.MeleePresentationTerminated -= HandleMeleePresentationTerminated;
            }
            outgoingMeleePresentations.Clear();
        }

        private void HandleMeleePresentationSpawned(MeleePresentationSpawn spawn)
        {
            outgoingMeleePresentations.Add(new NetworkMeleePresentationEdge
            {
                SourcePlayerId = netId,
                WeaponId = spawn.WeaponId,
                AttackEventId = spawn.Key.AttackEventId,
                SlashIndex = spawn.Key.SlashIndex,
                EventNetworkTime = NetworkTime.time,
                Phase = MeleePresentationPhase.Spawn,
                LocalPosition = spawn.LocalPosition,
                Direction = spawn.Direction,
                AnimationDuration = spawn.AnimationDuration,
                Stats = spawn.Stats
            });
        }

        private void HandleMeleePresentationTerminated(MeleePresentationTermination termination)
        {
            outgoingMeleePresentations.Add(new NetworkMeleePresentationEdge
            {
                SourcePlayerId = netId,
                WeaponId = termination.WeaponId,
                AttackEventId = termination.Key.AttackEventId,
                SlashIndex = termination.Key.SlashIndex,
                EventNetworkTime = NetworkTime.time,
                Phase = MeleePresentationPhase.Terminated
            });
        }

        private void FlushMeleePresentations()
        {
            if (outgoingMeleePresentations.Count == 0) return;
            for (int offset = 0; offset < outgoingMeleePresentations.Count;
                offset += maximumPresentationEdgesPerBatch)
            {
                int count = Math.Min(maximumPresentationEdgesPerBatch,
                    outgoingMeleePresentations.Count - offset);
                var edges = new NetworkMeleePresentationEdge[count];
                outgoingMeleePresentations.CopyTo(offset, edges, 0, count);
                outgoingMeleeBatchSequence = NextSequence(outgoingMeleeBatchSequence);
                CmdSubmitMeleePresentations(new NetworkMeleePresentationBatch
                {
                    BatchSequence = outgoingMeleeBatchSequence,
                    Edges = edges
                });
                SentMeleePresentationCount += count;
            }
            outgoingMeleePresentations.Clear();
        }

        [Command(channel = Channels.Reliable)]
        private void CmdSubmitMeleePresentations(NetworkMeleePresentationBatch batch,
            NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender != connectionToClient ||
                !IsValidMeleePresentationBatch(batch, netId,
                    lastServerMeleeBatchSequence, maximumPresentationEdgesPerBatch))
            {
                RejectMeleePresentations(batch.Edges?.Length ?? 1);
                return;
            }

            lastServerMeleeBatchSequence = batch.BatchSequence;
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            var admitted = new List<NetworkMeleePresentationEdge>(batch.Edges.Length);
            foreach (NetworkMeleePresentationEdge edge in batch.Edges)
            {
                // A previously admitted burst can finish after its build changes or an
                // upgrade menu opens. The immutable attack root governs those slashes.
                if (edge.Phase == MeleePresentationPhase.Spawn)
                {
                    if (world == null || !world.Gateway.Attacks.Contains(
                        netId, edge.AttackEventId, edge.WeaponId) ||
                        !serverMeleePresentationHistory.TryRecord(edge.Key))
                    {
                        RejectMeleePresentations(1);
                        continue;
                    }
                }
                admitted.Add(edge);
            }
            if (admitted.Count == 0) return;
            batch.Edges = admitted.ToArray();
            RpcApplyMeleePresentations(batch);
        }

        [ClientRpc(channel = Channels.Reliable)]
        private void RpcApplyMeleePresentations(NetworkMeleePresentationBatch batch)
        {
            if (isOwned) return;
            ApplyRemoteMeleePresentations(batch, NetworkTime.time);
        }

        private void ApplyRemoteMeleePresentations(NetworkMeleePresentationBatch batch,
            double currentNetworkTime)
        {
            if (!IsValidMeleePresentationBatch(batch, netId,
                lastClientMeleeBatchSequence, maximumPresentationEdgesPerBatch))
            {
                RejectMeleePresentations(batch.Edges?.Length ?? 1);
                return;
            }
            lastClientMeleeBatchSequence = batch.BatchSequence;
            RuntimeDB database = playerBootstrap != null
                ? playerBootstrap.ResolveSharedRuntimeDatabase() : null;
            if (database == null || playerMovement == null)
            {
                RejectMeleePresentations(batch.Edges.Length);
                return;
            }

            meleePresentationReplica ??= new MeleePresentationReplica(playerMovement, database);
            foreach (NetworkMeleePresentationEdge edge in batch.Edges)
            {
                ReceivedMeleePresentationCount++;
                if (edge.Phase == MeleePresentationPhase.Spawn)
                {
                    double elapsed = Math.Max(0d, currentNetworkTime - edge.EventNetworkTime);
                    if (elapsed > maximumReplayDelay ||
                        !meleePresentationReplica.TrySpawn(edge.ToSpawn(), (float)elapsed))
                        RejectMeleePresentations(1);
                    else ReplicaMeleeSpawnCount++;
                }
                else if (meleePresentationReplica.TryTerminate(edge.ToTermination()))
                    ReplicaMeleeTerminationCount++;
            }
        }

        public static bool IsValidMeleePresentationBatch(NetworkMeleePresentationBatch batch,
            uint expectedSourcePlayerId, uint previousBatchSequence, int maximumEdges)
        {
            if (expectedSourcePlayerId == 0u || maximumEdges <= 0 || batch.Edges == null ||
                batch.Edges.Length == 0 || batch.Edges.Length > maximumEdges ||
                !ProjectilePresentationSequence.IsNewer(batch.BatchSequence, previousBatchSequence))
                return false;
            foreach (NetworkMeleePresentationEdge edge in batch.Edges)
                if (!edge.IsValid || edge.SourcePlayerId != expectedSourcePlayerId) return false;
            return true;
        }

        private void DisposeMeleePresentationReplica()
        {
            meleePresentationReplica?.Dispose();
            meleePresentationReplica = null;
        }

        private void RejectMeleePresentations(int count)
        {
            RejectedMeleePresentationCount += count;
            RejectedPresentationCount += count;
        }

        private void OnDestroy()
        {
            DetachOwnerCallbacks();
            presentationReplica?.Dispose();
            presentationReplica = null;
            DisposeMeleePresentationReplica();
            DisposeBeamPresentationReplica();
            DisposeOrbitPresentationReplica();
            DisposeTrailPresentationReplica();
            DisposeSummonPresentationReplica();
        }
    }
}
