using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DefaultExecutionOrder(-9900)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed partial class NetworkEnemySimulationWorld : NetworkBehaviour
    {
        [SerializeField, Min(0.01f)] private float serverSnapshotInterval = 0.05f;
        [SerializeField, Range(1, 32)] private int maximumSnapshotsPerBatch = 20;
        [SerializeField, Range(1, 64)]
        private int maximumAttackPresentationEdgesPerBatch = 32;

        private readonly Dictionary<uint, NetworkEnemySimulationEndpoint> players =
            new Dictionary<uint, NetworkEnemySimulationEndpoint>();
        private readonly Dictionary<uint, NetworkEnemySimulationAgent> enemies =
            new Dictionary<uint, NetworkEnemySimulationAgent>();
        private readonly HashSet<uint> neverAssignedEnemies = new HashSet<uint>();
        private readonly List<uint> enemyIdBuffer = new List<uint>();
        private readonly List<EnemySimulationSnapshot> snapshotBuffer =
            new List<EnemySimulationSnapshot>(128);
        private readonly List<EnemyAttackPresentationEdge>
            attackPresentationBuffer =
                new List<EnemyAttackPresentationEdge>(64);
        private readonly Dictionary<uint, EnemyAttackPresentationEdge>
            pendingClientAttackPresentations =
                new Dictionary<uint, EnemyAttackPresentationEdge>();

        private double nextServerSnapshotTime;

        public static NetworkEnemySimulationWorld Instance { get; private set; }

        public ServerEnemySimulationRegistry Registry { get; private set; }

        public int PendingClientAttackPresentationCount =>
            pendingClientAttackPresentations.Count;

        public event Action<NetworkEnemySimulationEndpoint>
            ServerPlayerRegistered;

        public event Action<NetworkEnemySimulationEndpoint>
            ServerPlayerUnregistered;

        public bool HasEligiblePlayer
        {
            get
            {
                foreach (NetworkEnemySimulationEndpoint player in players.Values)
                {
                    if (IsEligibleEndpoint(player))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("Only one NetworkEnemySimulationWorld may be active.", this);
                enabled = false;
                return;
            }

            Instance = this;
            Registry = new ServerEnemySimulationRegistry();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            nextServerSnapshotTime = NetworkTime.time + serverSnapshotInterval;
            observedCombatGateway = NetworkCombatWorld.Instance?.Gateway;
            if (observedCombatGateway != null) observedCombatGateway.CombatResultAccepted += HandleAcceptedOrdinaryHit;
        }

        private ServerCombatGateway observedCombatGateway;

        public override void OnStartClient()
        {
            base.OnStartClient();
            ClearClientWaitingState();
            // This cleanup precedes the first running round; it must not suppress its trap replicas.
            stoppedTrapRound = uint.MaxValue;
        }

        public override void OnStopClient()
        {
            ClearClientWaitingState();
            base.OnStopClient();
        }

        private void ClearClientWaitingState()
        {
            ClearReferenceTraps();
            ClearEnemyProjectiles();
            pendingClientAttackPresentations.Clear();
            pendingClientKnockbacks.Clear();
            pendingKnockbackIds.Clear();
        }

        public override void OnStopServer()
        {
            ClearReferenceTraps();
            if (observedCombatGateway != null) observedCombatGateway.CombatResultAccepted -= HandleAcceptedOrdinaryHit;
            observedCombatGateway = null;
            acceptedProjectiles.Clear(); acceptedTerminations.Clear();
            pendingServerTerminations.Clear();
            handoffs.Clear();
            pendingServerKnockbacks.Clear();
            base.OnStopServer();
        }

        [Server]
        public void RegisterPlayer(NetworkEnemySimulationEndpoint endpoint)
        {
            if (endpoint == null || endpoint.netId == 0u)
            {
                throw new ArgumentException("A spawned Player endpoint is required.", nameof(endpoint));
            }

            uint playerId = endpoint.PlayerEntityId;
            bool changed = !players.TryGetValue(
                playerId,
                out NetworkEnemySimulationEndpoint current) ||
                current != endpoint;
            players[playerId] = endpoint;
            UpdateTargetDecisions();
            SendCachedAttackPresentations(endpoint);
            if (changed)
            {
                ServerPlayerRegistered?.Invoke(endpoint);
            }
        }

        [Server]
        public void UnregisterPlayer(NetworkEnemySimulationEndpoint endpoint)
        {
            if (endpoint == null)
            {
                return;
            }

            uint playerId = endpoint.PlayerEntityId;
            if (!players.TryGetValue(playerId, out NetworkEnemySimulationEndpoint current) ||
                current != endpoint)
            {
                return;
            }

            players.Remove(playerId);
            ForgetPlayerKnockbackPulses(playerId);
            ServerPlayerUnregistered?.Invoke(endpoint);
            UpdateTargetDecisions();
        }

        [Server]
        public void RegisterEnemy(NetworkEnemySimulationAgent enemy)
        {
            RegisterEnemy(enemy, 0u);
        }

        [Server]
        public void RegisterEnemy(
            NetworkEnemySimulationAgent enemy,
            uint preferredTargetPlayerId)
        {
            if (enemy == null || enemy.netId == 0u)
            {
                throw new ArgumentException("A spawned Enemy is required.", nameof(enemy));
            }

            uint enemyId = enemy.netId;
            enemies[enemyId] = enemy;
            Registry.RegisterEnemy(enemyId, enemy.transform.position, NetworkTime.time);
            NetworkEnemySimulationEndpoint target = preferredTargetPlayerId != 0u &&
                TryGetEligiblePlayer(
                    preferredTargetPlayerId,
                    out NetworkEnemySimulationEndpoint preferred)
                ? preferred
                : FindNearestEligiblePlayer(enemy.transform.position);
            EnemySimulationAssignment assignment;
            if (target == null)
            {
                neverAssignedEnemies.Add(enemyId);
                assignment = Registry.Freeze(enemyId);
            }
            else
            {
                assignment = AssignInitialHost(enemy, target);
            }

            PublishHandoff(enemy, assignment, EnemyTargetChangeReason.Spawn);
        }

        [Server]
        public bool TryGetEligiblePlayer(
            uint playerEntityId,
            out NetworkEnemySimulationEndpoint endpoint)
        {
            return players.TryGetValue(playerEntityId, out endpoint) &&
                IsEligibleEndpoint(endpoint);
        }

        [Server]
        public void GetEligiblePlayers(
            List<NetworkEnemySimulationEndpoint> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            foreach (NetworkEnemySimulationEndpoint endpoint in players.Values)
            {
                if (IsEligibleEndpoint(endpoint))
                {
                    results.Add(endpoint);
                }
            }
        }

        [Server]
        public void UnregisterEnemy(NetworkEnemySimulationAgent enemy)
        {
            if (enemy == null)
            {
                return;
            }

            enemies.Remove(enemy.netId);
            handoffs.Remove(enemy.netId);
            pendingServerKnockbacks.Remove(enemy.netId);
            pendingClientKnockbacks.Remove(enemy.netId);
            neverAssignedEnemies.Remove(enemy.netId);
            Registry.UnregisterEnemy(enemy.netId);
        }

        public void RegisterClientEnemy(NetworkEnemySimulationAgent enemy)
        {
            if (enemy != null && enemy.netId != 0u)
            {
                enemies[enemy.netId] = enemy;
                TryApplyPendingAttackPresentation(enemy);
            }
        }

        public void UnregisterClientEnemy(NetworkEnemySimulationAgent enemy)
        {
            if (enemy != null &&
                enemies.TryGetValue(enemy.netId, out NetworkEnemySimulationAgent current) &&
                current == enemy)
            {
                enemies.Remove(enemy.netId);
                handoffs.Remove(enemy.netId);
                pendingServerKnockbacks.Remove(enemy.netId);
                pendingClientAttackPresentations.Remove(enemy.netId);
                pendingClientKnockbacks.Remove(enemy.netId);
            }
        }

        [Client]
        public void TryApplyPendingAttackPresentation(
            NetworkEnemySimulationAgent enemy)
        {
            if (enemy == null || enemy.netId == 0u ||
                !pendingClientAttackPresentations.TryGetValue(
                    enemy.netId,
                    out EnemyAttackPresentationEdge edge))
            {
                return;
            }

            if ((edge.AssignmentEpoch != enemy.Assignment.Epoch &&
                 !EnemySimulationSequence.IsNewer(edge.AssignmentEpoch, enemy.Assignment.Epoch)) ||
                enemy.ReceiveRemoteAttackPresentation(edge))
            {
                pendingClientAttackPresentations.Remove(enemy.netId);
            }
        }

        [Client]
        public void CollectClientOwnedSnapshots(
            uint playerEntityId,
            double networkTime,
            List<EnemySimulationSnapshot> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            foreach (NetworkEnemySimulationAgent enemy in enemies.Values)
            {
                if (enemy == null ||
                    enemy.Assignment.Host != EnemySimulationHost.ClientPlayer ||
                    enemy.Assignment.SimulationOwnerPlayerId != playerEntityId ||
                    !enemy.IsCanonicalAlive)
                {
                    continue;
                }

                if (enemy.TryCaptureSnapshot(networkTime, out EnemySimulationSnapshot snapshot))
                {
                    results.Add(snapshot);
                }
            }
        }

        [Client]
        public void CollectClientOwnedAttackPresentations(
            uint playerEntityId,
            List<EnemyAttackPresentationEdge> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            foreach (NetworkEnemySimulationAgent enemy in enemies.Values)
            {
                if (enemy == null ||
                    enemy.Assignment.Host != EnemySimulationHost.ClientPlayer ||
                    enemy.Assignment.SimulationOwnerPlayerId != playerEntityId ||
                    !enemy.IsCanonicalAlive)
                {
                    continue;
                }

                while (enemy.TryDequeueAttackPresentation(
                    out EnemyAttackPresentationEdge edge))
                {
                    results.Add(edge);
                }
            }
        }

        [Server]
        public void SubmitClientSnapshots(
            NetworkEnemySimulationEndpoint endpoint,
            EnemySimulationSnapshotBatch batch)
        {
            if (BootGameplayNetworkManager.CombatHasEnded) return;
            // Batch datagrams may arrive out of order and contain disjoint Enemies.
            // Per-Enemy epoch/sequence validation in the Registry provides idempotency.
            if (endpoint == null ||
                !players.TryGetValue(
                    endpoint.PlayerEntityId,
                    out NetworkEnemySimulationEndpoint registered) ||
                registered != endpoint ||
                batch.Snapshots == null || batch.Snapshots.Length == 0 ||
                batch.Snapshots.Length > maximumSnapshotsPerBatch ||
                batch.BatchSequence == 0u)
            {
                return;
            }

            snapshotBuffer.Clear();
            for (int i = 0; i < batch.Snapshots.Length; i++)
            {
                EnemySimulationSnapshot snapshot = batch.Snapshots[i];
                if (!enemies.TryGetValue(
                    snapshot.EnemyEntityId,
                    out NetworkEnemySimulationAgent enemy) ||
                    enemy == null || !enemy.IsCanonicalAlive || !IsServerEnemyAlive(enemy.netId))
                {
                    continue;
                }

                if (!enemy.ValidateDashAction(snapshot.Runtime.Action)) continue;
                var rejection = Registry.TryAcceptClientSnapshot(endpoint.PlayerEntityId, snapshot);
                var progress = GetHandoff(snapshot.EnemyEntityId);
                if (rejection == EnemySnapshotRejectionReason.WrongOwner) progress.Diagnostics.WrongOwner++;
                if (rejection == EnemySnapshotRejectionReason.WrongEpoch) progress.Diagnostics.WrongEpoch++;
                if (rejection == EnemySnapshotRejectionReason.None)
                {
                    GetHandoff(snapshot.EnemyEntityId).Observe(snapshot.AssignmentEpoch, NetworkTime.time);
                    AcknowledgeKnockback(snapshot);
                    snapshotBuffer.Add(snapshot);
                }
            }

            BroadcastSnapshots(snapshotBuffer);
        }

        [Server]
        public void SubmitClientAttackPresentations(
            NetworkEnemySimulationEndpoint endpoint,
            EnemyAttackPresentationBatch batch)
        {
            if (BootGameplayNetworkManager.CombatHasEnded) return;
            if (endpoint == null ||
                !players.TryGetValue(
                    endpoint.PlayerEntityId,
                    out NetworkEnemySimulationEndpoint registered) ||
                registered != endpoint ||
                (batch.Edges?.Length ?? 0) > maximumAttackPresentationEdgesPerBatch ||
                (batch.ProjectileLaunches?.Length ?? 0) > maximumAttackPresentationEdgesPerBatch ||
                (batch.ProjectileTerminations?.Length ?? 0) > maximumAttackPresentationEdgesPerBatch ||
                batch.BatchSequence == 0u)
            {
                return;
            }

            SubmitEnemyProjectiles(endpoint.PlayerEntityId, batch.ProjectileLaunches);
            SubmitEnemyProjectileTerminations(endpoint.PlayerEntityId, batch.ProjectileTerminations);
            attackPresentationBuffer.Clear();
            for (int i = 0; i < (batch.Edges?.Length ?? 0); i++)
            {
                EnemyAttackPresentationEdge edge = batch.Edges[i];
                if (!enemies.TryGetValue(
                    edge.EnemyEntityId,
                    out NetworkEnemySimulationAgent enemy) ||
                    enemy == null || !enemy.IsCanonicalAlive || !IsServerEnemyAlive(enemy.netId))
                {
                    continue;
                }

                if (!enemy.ValidateDashAction(edge.Checkpoint.Movement.Runtime.Action)) continue;
                if (Registry.TryAcceptClientAttackPresentation(
                    endpoint.PlayerEntityId,
                    edge) == EnemyAttackPresentationRejectionReason.None)
                {
                    attackPresentationBuffer.Add(edge);
                }
            }

            BroadcastAttackPresentations(attackPresentationBuffer);
        }

        [ServerCallback]
        private void Update()
        {
            UpdateReferenceClock();
            if (BootGameplayNetworkManager.CombatHasEnded) return;
            UpdateReferenceProjectiles();
            UpdateTargetDecisions();
            BroadcastServerAttackPresentations();
            if (NetworkTime.time < nextServerSnapshotTime)
            {
                return;
            }

            nextServerSnapshotTime = NetworkTime.time + serverSnapshotInterval;
            snapshotBuffer.Clear();
            foreach (NetworkEnemySimulationAgent enemy in enemies.Values)
            {
                if (enemy == null || !enemy.IsCanonicalAlive || !IsServerEnemyAlive(enemy.netId) ||
                    (enemy.Assignment.Host != EnemySimulationHost.ServerFallback &&
                     enemy.Assignment.Host != EnemySimulationHost.ServerAuthoritative))
                {
                    continue;
                }

                if (enemy.TryCaptureSnapshot(
                    NetworkTime.time,
                    out EnemySimulationSnapshot snapshot))
                {
                    Registry.RecordServerSnapshot(snapshot);
                    GetHandoff(snapshot.EnemyEntityId).Observe(snapshot.AssignmentEpoch, NetworkTime.time);
                    AcknowledgeKnockback(snapshot);
                    snapshotBuffer.Add(snapshot);
                }
            }

            BroadcastSnapshots(snapshotBuffer);
        }

        [Server]
        private void BroadcastServerAttackPresentations()
        {
            attackPresentationBuffer.Clear();
            foreach (NetworkEnemySimulationAgent enemy in enemies.Values)
            {
                if (enemy == null || !enemy.IsCanonicalAlive || !IsServerEnemyAlive(enemy.netId) ||
                    (enemy.Assignment.Host != EnemySimulationHost.ServerFallback &&
                     enemy.Assignment.Host != EnemySimulationHost.ServerAuthoritative))
                {
                    continue;
                }

                while (enemy.TryDequeueAttackPresentation(
                    out EnemyAttackPresentationEdge edge))
                {
                    Registry.RecordServerAttackPresentation(edge);
                    attackPresentationBuffer.Add(edge);
                }
            }

            BroadcastAttackPresentations(attackPresentationBuffer);
        }

        [Server]
        private EnemySimulationAssignment AssignInitialHost(
            NetworkEnemySimulationAgent enemy,
            NetworkEnemySimulationEndpoint target)
        {
            return enemy.SimulationMode == EnemySimulationMode.BossServer
                ? Registry.AssignServerAuthoritative(
                    enemy.netId,
                    target.PlayerEntityId)
                : Registry.AssignClientOwner(
                    enemy.netId,
                    target.PlayerEntityId,
                    target.PlayerEntityId);
        }

        [Server]
        private NetworkEnemySimulationEndpoint FindNearestEligiblePlayer(Vector2 position)
        {
            var nearest = new EnemyNearestTarget();
            foreach (NetworkEnemySimulationEndpoint player in players.Values)
            {
                if (!IsEligibleEndpoint(player))
                {
                    continue;
                }

                nearest.Consider(player.netId, StableParticipantId(player), player.transform.position, position, true);
            }
            return nearest.AvatarId != 0 ? players[nearest.AvatarId] : null;
        }

        [Server]
        private void BroadcastSnapshots(List<EnemySimulationSnapshot> snapshots)
        {
            EnemySimulationWire.SendBatches(snapshots, maximumSnapshotsPerBatch, (batch, reliable) =>
            {
                batch.Round = CurrentRound;
                if (reliable) RpcApplyLargeSnapshot(batch); else RpcApplySnapshots(batch);
            });
        }

        [ClientRpc(channel = Channels.Reliable)]
        private void RpcApplyLargeSnapshot(EnemySimulationSnapshotBatch batch) => ApplyMovementSnapshots(batch);

        [Server]
        private void BroadcastAttackPresentations(
            List<EnemyAttackPresentationEdge> edges)
        {
            for (int offset = 0; offset < edges.Count;
                 offset += maximumAttackPresentationEdgesPerBatch)
            {
                int count = Math.Min(
                    maximumAttackPresentationEdgesPerBatch,
                    edges.Count - offset);
                var packet = new EnemyAttackPresentationEdge[count];
                edges.CopyTo(offset, packet, 0, count);
                RpcApplyAttackPresentations(new EnemyAttackPresentationBatch
                { Round = CurrentRound,
                    Edges = packet
                });
            }
        }

        [ClientRpc(channel = Channels.Unreliable)]
        private void RpcApplySnapshots(EnemySimulationSnapshotBatch batch) => ApplyMovementSnapshots(batch);

        private void ApplyMovementSnapshots(EnemySimulationSnapshotBatch batch)
        {
            if (batch.Round != CurrentRound) return;
            if (BootGameplayNetworkManager.CombatHasEnded) return;
            if (batch.Snapshots == null)
            {
                return;
            }

            for (int i = 0; i < batch.Snapshots.Length; i++)
            {
                EnemySimulationSnapshot snapshot = batch.Snapshots[i];
                if (enemies.TryGetValue(
                    snapshot.EnemyEntityId,
                    out NetworkEnemySimulationAgent enemy) &&
                    enemy != null)
                {
                    enemy.ReceiveRemoteSnapshot(snapshot);
                }
            }
        }

        [ClientRpc(channel = Channels.Reliable)]
        private void RpcApplyAttackPresentations(
            EnemyAttackPresentationBatch batch)
        {
            ApplyAttackPresentations(batch);
        }

        [TargetRpc(channel = Channels.Reliable)]
        private void TargetApplyAttackPresentations(
            NetworkConnectionToClient target,
            EnemyAttackPresentationBatch batch)
        {
            ApplyAttackPresentations(batch);
        }

        [Client]
        private void ApplyAttackPresentations(
            EnemyAttackPresentationBatch batch)
        {
            if (batch.Round != CurrentRound) return;
            if (BootGameplayNetworkManager.CombatHasEnded) return;
            if (batch.ProjectileTerminations != null)
                foreach (var terminal in batch.ProjectileTerminations) ApplyEnemyProjectileTermination(terminal);
            if (batch.ProjectileLaunches != null)
                foreach (var launch in batch.ProjectileLaunches) PresentEnemyProjectile(launch);
            if (batch.Edges == null) return;

            for (int i = 0; i < batch.Edges.Length; i++)
            {
                EnemyAttackPresentationEdge edge = batch.Edges[i];
                if (enemies.TryGetValue(
                    edge.EnemyEntityId,
                    out NetworkEnemySimulationAgent enemy) &&
                    enemy != null)
                {
                    if (!enemy.ReceiveRemoteAttackPresentation(edge))
                    {
                        // A future assignment edge can race the SyncVar hook.
                        // Same/older epoch rejection is stale or duplicate and
                        // must not remain in the pending cache forever.
                        if (edge.AssignmentEpoch > enemy.Assignment.Epoch ||
                            (!enemy.IsCanonicalAlive &&
                             edge.AssignmentEpoch == enemy.Assignment.Epoch))
                        {
                            CachePendingAttackPresentation(edge);
                        }
                    }
                }
                else
                {
                    // A reliable TargetRpc used for Late Join can arrive before
                    // the corresponding Enemy spawn has completed locally. Keep
                    // only the newest state for that Enemy and apply it from
                    // RegisterClientEnemy instead of losing the last known phase.
                    CachePendingAttackPresentation(edge);
                }
            }
        }

        [Client]
        private void CachePendingAttackPresentation(
            EnemyAttackPresentationEdge edge)
        {
            if (!edge.IsFinite || !edge.HasKnownPhase ||
                edge.EnemyEntityId == 0u || edge.AssignmentEpoch == 0u ||
                edge.StateSequence == 0u)
            {
                return;
            }

            if (pendingClientAttackPresentations.TryGetValue(
                edge.EnemyEntityId,
                out EnemyAttackPresentationEdge current) &&
                (edge.AssignmentEpoch < current.AssignmentEpoch ||
                 (edge.AssignmentEpoch == current.AssignmentEpoch &&
                  !EnemySimulationSequence.IsNewer(
                      edge.StateSequence,
                      current.StateSequence))))
            {
                return;
            }

            pendingClientAttackPresentations[edge.EnemyEntityId] = edge;
        }

        [Server]
        private void SendCachedAttackPresentations(
            NetworkEnemySimulationEndpoint endpoint)
        {
            if (endpoint == null || endpoint.connectionToClient == null)
            {
                return;
            }

            SendReferenceFlights(endpoint);
            Registry.GetLatestAttackPresentations(attackPresentationBuffer);
            for (int offset = 0; offset < attackPresentationBuffer.Count;
                 offset += maximumAttackPresentationEdgesPerBatch)
            {
                int count = Math.Min(
                    maximumAttackPresentationEdgesPerBatch,
                    attackPresentationBuffer.Count - offset);
                var packet = new EnemyAttackPresentationEdge[count];
                attackPresentationBuffer.CopyTo(offset, packet, 0, count);
                TargetApplyAttackPresentations(
                    endpoint.connectionToClient,
                    new EnemyAttackPresentationBatch
                    { Round = CurrentRound,
                        Edges = packet
                    });
            }
        }

        private void OnDestroy()
        {
            ClearReferenceTraps();
            if (observedCombatGateway != null) observedCombatGateway.CombatResultAccepted -= HandleAcceptedOrdinaryHit;
            observedCombatGateway = null;
            ClearEnemyProjectiles();
            ClearUltimateKnockbackState();
            handoffs.Clear();
            pendingServerKnockbacks.Clear();
            players.Clear();
            enemies.Clear();
            neverAssignedEnemies.Clear();
            pendingClientAttackPresentations.Clear();
            ServerPlayerRegistered = null;
            ServerPlayerUnregistered = null;
            ClearReferenceClock();
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
