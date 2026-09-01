using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MirrorNetworkCombatBridge))]
    [RequireComponent(typeof(PlayerBuildRuntime))]
    public sealed class NetworkWeaponCombatAdapter : NetworkBehaviour
    {
        [SerializeField] private MirrorNetworkCombatBridge bridge;
        [SerializeField] private CombatRuntimeServiceProvider serviceProvider;
        [SerializeField] private PlayerBuildRuntime playerBuildRuntime;
        [SerializeField] private NetworkPlayerBootstrap playerBootstrap;
        [SerializeField] private PlayerMovement playerMovement;
        [SerializeField, Range(1, 32)]
        private int maximumPresentationEdgesPerBatch = 32;
        [SerializeField, Min(0.1f)] private float maximumReplayDelay = 2f;

        private readonly List<NetworkProjectilePresentationEdge>
            outgoingPresentations =
                new List<NetworkProjectilePresentationEdge>(64);
        private ProjectilePresentationReplica presentationReplica;
        private uint outgoingBatchSequence;
        private uint lastServerBatchSequence;
        private uint lastClientBatchSequence;

        public int SentPresentationCount { get; private set; }
        public int ReceivedPresentationCount { get; private set; }
        public int ReplicaSpawnCount { get; private set; }
        public int ReplicaTerminationCount { get; private set; }
        public int RejectedPresentationCount { get; private set; }
        public int ReplicaActiveProjectileCount =>
            presentationReplica?.ActiveProjectileCount ?? 0;

        private void Awake()
        {
            if (bridge == null)
            {
                bridge = GetComponent<MirrorNetworkCombatBridge>();
            }

            if (serviceProvider == null)
            {
                serviceProvider = GetComponent<CombatRuntimeServiceProvider>();
            }

            if (serviceProvider == null)
            {
                serviceProvider = gameObject.AddComponent<
                    CombatRuntimeServiceProvider>();
            }

            if (playerBuildRuntime == null)
            {
                playerBuildRuntime = GetComponent<PlayerBuildRuntime>();
            }

            if (playerBootstrap == null)
            {
                playerBootstrap = GetComponent<NetworkPlayerBootstrap>();
            }

            if (playerMovement == null)
            {
                playerMovement = GetComponent<PlayerMovement>();
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            lastClientBatchSequence = 0u;
        }

        public override void OnStartAuthority()
        {
            base.OnStartAuthority();
            bridge.OwnerCollectorReady += HandleCollectorReady;
            if (bridge.Collector != null)
            {
                HandleCollectorReady(bridge.Collector, bridge.EventIds);
            }

            if (playerBuildRuntime != null)
            {
                playerBuildRuntime.ProjectilePresentationSpawned +=
                    HandlePresentationSpawned;
                playerBuildRuntime.ProjectilePresentationTerminated +=
                    HandlePresentationTerminated;
            }
        }

        [ClientCallback]
        private void Update()
        {
            if (isOwned)
            {
                FlushPresentations();
            }
        }

        private void HandlePresentationSpawned(
            ProjectilePresentationSpawn spawn)
        {
            outgoingPresentations.Add(new NetworkProjectilePresentationEdge
            {
                SourcePlayerId = netId,
                WeaponId = spawn.WeaponId,
                AttackEventId = spawn.Key.AttackEventId,
                ProjectileIndex = spawn.Key.ProjectileIndex,
                EventNetworkTime = NetworkTime.time,
                Phase = ProjectilePresentationPhase.Spawn,
                Position = spawn.Position,
                Direction = spawn.Direction,
                Element = spawn.Element,
                RotateToMovement = spawn.RotateToMovement,
                Stats = spawn.Stats
            });
        }

        private void HandlePresentationTerminated(
            ProjectilePresentationTermination termination)
        {
            outgoingPresentations.Add(new NetworkProjectilePresentationEdge
            {
                SourcePlayerId = netId,
                WeaponId = termination.WeaponId,
                AttackEventId = termination.Key.AttackEventId,
                ProjectileIndex = termination.Key.ProjectileIndex,
                EventNetworkTime = NetworkTime.time,
                Phase = termination.Phase,
                Position = termination.Position
            });
        }

        private void FlushPresentations()
        {
            if (outgoingPresentations.Count == 0)
            {
                return;
            }

            for (int offset = 0; offset < outgoingPresentations.Count;
                 offset += maximumPresentationEdgesPerBatch)
            {
                int count = Math.Min(
                    maximumPresentationEdgesPerBatch,
                    outgoingPresentations.Count - offset);
                var edges = new NetworkProjectilePresentationEdge[count];
                outgoingPresentations.CopyTo(offset, edges, 0, count);
                outgoingBatchSequence = NextSequence(outgoingBatchSequence);
                CmdSubmitProjectilePresentations(
                    new NetworkProjectilePresentationBatch
                    {
                        BatchSequence = outgoingBatchSequence,
                        Edges = edges
                    });
                SentPresentationCount += count;
            }

            outgoingPresentations.Clear();
        }

        [Command(channel = Channels.Reliable)]
        private void CmdSubmitProjectilePresentations(
            NetworkProjectilePresentationBatch batch,
            NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender != connectionToClient ||
                !IsValidPresentationBatch(
                    batch,
                    netId,
                    lastServerBatchSequence,
                    maximumPresentationEdgesPerBatch))
            {
                RejectedPresentationCount += batch.Edges?.Length ?? 1;
                return;
            }

            lastServerBatchSequence = batch.BatchSequence;
            RpcApplyProjectilePresentations(batch);
        }

        [ClientRpc(channel = Channels.Reliable)]
        private void RpcApplyProjectilePresentations(
            NetworkProjectilePresentationBatch batch)
        {
            if (isOwned)
            {
                return;
            }

            ApplyRemotePresentations(batch, NetworkTime.time);
        }

        private void ApplyRemotePresentations(
            NetworkProjectilePresentationBatch batch,
            double currentNetworkTime)
        {
            if (!IsValidPresentationBatch(
                    batch,
                    netId,
                    lastClientBatchSequence,
                    maximumPresentationEdgesPerBatch))
            {
                RejectedPresentationCount += batch.Edges?.Length ?? 1;
                return;
            }

            lastClientBatchSequence = batch.BatchSequence;
            RuntimeDB database = playerBootstrap != null
                ? playerBootstrap.ResolveSharedRuntimeDatabase()
                : null;
            if (database == null || playerMovement == null)
            {
                RejectedPresentationCount += batch.Edges.Length;
                return;
            }

            presentationReplica ??=
                new ProjectilePresentationReplica(playerMovement, database);
            for (int i = 0; i < batch.Edges.Length; i++)
            {
                NetworkProjectilePresentationEdge edge = batch.Edges[i];
                ReceivedPresentationCount++;
                if (edge.Phase == ProjectilePresentationPhase.Spawn)
                {
                    double elapsed = Math.Max(
                        0d,
                        currentNetworkTime - edge.EventNetworkTime);
                    if (elapsed > maximumReplayDelay ||
                        !presentationReplica.TrySpawn(
                            edge.ToSpawn(),
                            (float)elapsed))
                    {
                        RejectedPresentationCount++;
                        continue;
                    }

                    ReplicaSpawnCount++;
                    continue;
                }

                if (presentationReplica.TryTerminate(edge.ToTermination()))
                {
                    ReplicaTerminationCount++;
                }
            }
        }

        public static bool IsValidPresentationBatch(
            NetworkProjectilePresentationBatch batch,
            uint expectedSourcePlayerId,
            uint previousBatchSequence,
            int maximumEdges)
        {
            if (expectedSourcePlayerId == 0u || maximumEdges <= 0 ||
                batch.Edges == null || batch.Edges.Length == 0 ||
                batch.Edges.Length > maximumEdges ||
                !ProjectilePresentationSequence.IsNewer(
                    batch.BatchSequence,
                    previousBatchSequence))
            {
                return false;
            }

            for (int i = 0; i < batch.Edges.Length; i++)
            {
                if (!batch.Edges[i].IsValid ||
                    batch.Edges[i].SourcePlayerId != expectedSourcePlayerId)
                {
                    return false;
                }
            }

            return true;
        }

        public override void OnStopAuthority()
        {
            if (bridge != null)
            {
                bridge.OwnerCollectorReady -= HandleCollectorReady;
            }

            if (playerBuildRuntime != null)
            {
                playerBuildRuntime.ProjectilePresentationSpawned -=
                    HandlePresentationSpawned;
                playerBuildRuntime.ProjectilePresentationTerminated -=
                    HandlePresentationTerminated;
            }

            outgoingPresentations.Clear();
            base.OnStopAuthority();
        }

        public override void OnStopClient()
        {
            presentationReplica?.Dispose();
            presentationReplica = null;
            base.OnStopClient();
        }

        private void HandleCollectorReady(
            ClientCombatCollector collector,
            MonsterSupergroup.GAS.ICombatEventIdSource eventIds)
        {
            var services = new CombatRuntimeServices(
                bridge.OwnerPlayerId,
                bridge.SourceEntityId,
                eventIds,
                collector);
            serviceProvider.Configure(services);
        }

        private static uint NextSequence(uint value)
        {
            value = unchecked(value + 1u);
            return value == 0u ? 1u : value;
        }
    }
}
