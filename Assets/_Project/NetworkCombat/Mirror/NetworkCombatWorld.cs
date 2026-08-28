using System;
using Mirror;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class NetworkCombatWorld : NetworkBehaviour
    {
        [SerializeField, Min(0.01f)] private float serverTickInterval = 0.05f;

        private double nextServerTick;
        private ushort nextConnectionEpoch = 1;

        public static NetworkCombatWorld Instance { get; private set; }

        public ServerCombatGateway Gateway { get; private set; }
        public CanonicalWorldReplica Replica { get; } = new CanonicalWorldReplica();

        public event Action<CanonicalWorldBatch> CanonicalBatchReceived;
        public event Action<CanonicalWorldBatch> ServerCanonicalBatchProduced;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("Only one NetworkCombatWorld may be active.", this);
                enabled = false;
                return;
            }

            Instance = this;
            Gateway = new ServerCombatGateway();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            nextServerTick = NetworkTime.time;
        }

        [Server]
        public ushort AllocateConnectionEpoch()
        {
            ushort result = nextConnectionEpoch++;
            if (nextConnectionEpoch == 0)
            {
                nextConnectionEpoch = 1;
            }

            return result;
        }

        [Server]
        public CanonicalEntityState RegisterEntity(
            uint entityId,
            int maximumHealth,
            CombatEntityKind kind,
            CombatEntityAuthority authority,
            uint ownerPlayerId = 0)
        {
            CanonicalEntityState state = Gateway.Ledger.RegisterEntity(
                entityId,
                maximumHealth,
                kind,
                authority,
                ownerPlayerId);
            Broadcast(Gateway.CreateEntityUpdate(state));
            return state;
        }

        [Server]
        public void SendSnapshot(NetworkConnectionToClient connection)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            TargetApplyCanonical(connection, Gateway.CreateSnapshot());
        }

        [Server]
        public void ProcessSubmission(uint senderPlayerId, CombatSubmissionBatch batch)
        {
            Broadcast(Gateway.ProcessBatch(senderPlayerId, batch, NetworkTime.time));
        }

        [Server]
        public void HandleSourceDisconnected(uint sourcePlayerId)
        {
            Broadcast(Gateway.HandleSourceDisconnected(sourcePlayerId, NetworkTime.time));
        }

        [ServerCallback]
        private void Update()
        {
            double now = NetworkTime.time;
            if (now < nextServerTick)
            {
                return;
            }

            nextServerTick = now + serverTickInterval;
            Broadcast(Gateway.Advance(now));
        }

        [Server]
        private void Broadcast(CanonicalWorldBatch batch)
        {
            if (IsEmpty(batch))
            {
                return;
            }

            RpcApplyCanonical(batch);
            ServerCanonicalBatchProduced?.Invoke(batch);
        }

        [ClientRpc]
        private void RpcApplyCanonical(CanonicalWorldBatch batch)
        {
            ApplyCanonical(batch);
        }

        [TargetRpc]
        private void TargetApplyCanonical(
            NetworkConnectionToClient target,
            CanonicalWorldBatch batch)
        {
            ApplyCanonical(batch);
        }

        private void ApplyCanonical(CanonicalWorldBatch batch)
        {
            Replica.Apply(batch);
            CanonicalBatchReceived?.Invoke(batch);
        }

        private static bool IsEmpty(CanonicalWorldBatch batch)
        {
            return (batch.Entities == null || batch.Entities.Length == 0) &&
                (batch.Statuses == null || batch.Statuses.Length == 0) &&
                (batch.ConfirmedKills == null || batch.ConfirmedKills.Length == 0);
        }

        private void OnDestroy()
        {
            Replica.Clear();
            ServerCanonicalBatchProduced = null;
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
