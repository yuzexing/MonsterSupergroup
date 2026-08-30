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
    public sealed class NetworkEnemySimulationWorld : NetworkBehaviour
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

        public bool HasEligiblePlayer
        {
            get
            {
                foreach (NetworkEnemySimulationEndpoint player in players.Values)
                {
                    if (player != null && player.IsEligibleSimulationOwner)
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
        }

        [Server]
        public void RegisterPlayer(NetworkEnemySimulationEndpoint endpoint)
        {
            if (endpoint == null || endpoint.netId == 0u)
            {
                throw new ArgumentException("A spawned Player endpoint is required.", nameof(endpoint));
            }

            players[endpoint.PlayerEntityId] = endpoint;
            ResumeFrozenEnemies();
            SendCachedAttackPresentations(endpoint);
        }

        [Server]
        public void UnregisterPlayer(NetworkEnemySimulationEndpoint endpoint)
        {
            if (endpoint == null)
            {
                return;
            }

            uint playerId = endpoint.PlayerEntityId;
            players.Remove(playerId);
            Registry.GetEnemiesDependingOnPlayer(playerId, enemyIdBuffer);
            for (int i = 0; i < enemyIdBuffer.Count; i++)
            {
                uint enemyId = enemyIdBuffer[i];
                if (!enemies.TryGetValue(enemyId, out NetworkEnemySimulationAgent enemy) ||
                    enemy == null)
                {
                    continue;
                }

                if (Registry.TryGetLatestSnapshot(
                    enemyId,
                    out EnemySimulationSnapshot latest))
                {
                    enemy.SnapServerSimulationTo(latest);
                }

                NetworkEnemySimulationEndpoint target = FindNearestEligiblePlayer(
                    Registry.TryGetLatestSnapshot(enemyId, out latest)
                        ? latest.Position
                        : (Vector2)enemy.transform.position);
                EnemySimulationAssignment assignment;
                if (target == null)
                {
                    assignment = Registry.Freeze(enemyId);
                }
                else if (enemy.SimulationMode == EnemySimulationMode.BossServer)
                {
                    assignment = Registry.AssignServerAuthoritative(
                        enemyId,
                        target.PlayerEntityId);
                }
                else
                {
                    assignment = Registry.AssignServerFallback(
                        enemyId,
                        target.PlayerEntityId);
                }
                enemy.SetServerAssignment(assignment);
            }
        }

        [Server]
        public void RegisterEnemy(NetworkEnemySimulationAgent enemy)
        {
            if (enemy == null || enemy.netId == 0u)
            {
                throw new ArgumentException("A spawned Enemy is required.", nameof(enemy));
            }

            uint enemyId = enemy.netId;
            enemies[enemyId] = enemy;
            Registry.RegisterEnemy(enemyId, enemy.transform.position, NetworkTime.time);
            NetworkEnemySimulationEndpoint target =
                FindNearestEligiblePlayer(enemy.transform.position);
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

            enemy.SetServerAssignment(assignment);
        }

        [Server]
        public void UnregisterEnemy(NetworkEnemySimulationAgent enemy)
        {
            if (enemy == null)
            {
                return;
            }

            enemies.Remove(enemy.netId);
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
                pendingClientAttackPresentations.Remove(enemy.netId);
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

            if (enemy.ReceiveRemoteAttackPresentation(edge))
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
                    enemy == null || !enemy.IsCanonicalAlive)
                {
                    continue;
                }

                if (Registry.TryAcceptClientSnapshot(
                    endpoint.PlayerEntityId,
                    snapshot) == EnemySnapshotRejectionReason.None)
                {
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
            if (endpoint == null ||
                !players.TryGetValue(
                    endpoint.PlayerEntityId,
                    out NetworkEnemySimulationEndpoint registered) ||
                registered != endpoint || batch.Edges == null ||
                batch.Edges.Length == 0 ||
                batch.Edges.Length > maximumAttackPresentationEdgesPerBatch ||
                batch.BatchSequence == 0u)
            {
                return;
            }

            attackPresentationBuffer.Clear();
            for (int i = 0; i < batch.Edges.Length; i++)
            {
                EnemyAttackPresentationEdge edge = batch.Edges[i];
                if (!enemies.TryGetValue(
                    edge.EnemyEntityId,
                    out NetworkEnemySimulationAgent enemy) ||
                    enemy == null || !enemy.IsCanonicalAlive)
                {
                    continue;
                }

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
            BroadcastServerAttackPresentations();
            if (NetworkTime.time < nextServerSnapshotTime)
            {
                return;
            }

            nextServerSnapshotTime = NetworkTime.time + serverSnapshotInterval;
            snapshotBuffer.Clear();
            foreach (NetworkEnemySimulationAgent enemy in enemies.Values)
            {
                if (enemy == null || !enemy.IsCanonicalAlive ||
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
                if (enemy == null || !enemy.IsCanonicalAlive ||
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
        private void ResumeFrozenEnemies()
        {
            if (!HasEligiblePlayer)
            {
                return;
            }

            enemyIdBuffer.Clear();
            foreach (KeyValuePair<uint, NetworkEnemySimulationAgent> pair in enemies)
            {
                if (pair.Value != null &&
                    Registry.TryGetAssignment(pair.Key, out EnemySimulationAssignment current) &&
                    current.Host == EnemySimulationHost.Frozen)
                {
                    enemyIdBuffer.Add(pair.Key);
                }
            }

            for (int i = 0; i < enemyIdBuffer.Count; i++)
            {
                uint enemyId = enemyIdBuffer[i];
                NetworkEnemySimulationAgent enemy = enemies[enemyId];
                NetworkEnemySimulationEndpoint target =
                    FindNearestEligiblePlayer(enemy.transform.position);
                if (target == null)
                {
                    continue;
                }

                EnemySimulationAssignment assignment;
                if (enemy.SimulationMode == EnemySimulationMode.BossServer)
                {
                    neverAssignedEnemies.Remove(enemyId);
                    assignment = Registry.AssignServerAuthoritative(
                        enemyId,
                        target.PlayerEntityId);
                }
                else if (neverAssignedEnemies.Remove(enemyId))
                {
                    assignment = AssignInitialHost(enemy, target);
                }
                else
                {
                    if (Registry.TryGetLatestSnapshot(
                        enemyId,
                        out EnemySimulationSnapshot latest))
                    {
                        enemy.SnapServerSimulationTo(latest);
                    }
                    assignment = Registry.AssignServerFallback(
                        enemyId,
                        target.PlayerEntityId);
                }
                enemy.SetServerAssignment(assignment);
            }
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
            NetworkEnemySimulationEndpoint nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (NetworkEnemySimulationEndpoint player in players.Values)
            {
                if (player == null || !player.IsEligibleSimulationOwner)
                {
                    continue;
                }

                float distance = ((Vector2)player.transform.position - position).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = player;
                }
            }
            return nearest;
        }

        [Server]
        private void BroadcastSnapshots(List<EnemySimulationSnapshot> snapshots)
        {
            for (int offset = 0; offset < snapshots.Count;
                 offset += maximumSnapshotsPerBatch)
            {
                int count = Math.Min(
                    maximumSnapshotsPerBatch,
                    snapshots.Count - offset);
                var packet = new EnemySimulationSnapshot[count];
                snapshots.CopyTo(offset, packet, 0, count);
                RpcApplySnapshots(new EnemySimulationSnapshotBatch
                {
                    Snapshots = packet
                });
            }
        }

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
                {
                    Edges = packet
                });
            }
        }

        [ClientRpc(channel = Channels.Unreliable)]
        private void RpcApplySnapshots(EnemySimulationSnapshotBatch batch)
        {
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
            if (batch.Edges == null)
            {
                return;
            }

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
                    {
                        Edges = packet
                    });
            }
        }

        private void OnDestroy()
        {
            players.Clear();
            enemies.Clear();
            neverAssignedEnemies.Clear();
            pendingClientAttackPresentations.Clear();
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
