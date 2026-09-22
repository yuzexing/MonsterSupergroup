using System;
using Mirror;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed partial class NetworkCombatWorld : NetworkBehaviour
    {
        [SerializeField, Min(0.01f)] private float serverTickInterval = 0.05f;

        private double nextServerTick;
        private ushort nextConnectionEpoch = 1;

        public static NetworkCombatWorld Instance { get; private set; }

        public ServerCombatGateway Gateway { get; private set; }
        public CanonicalWorldReplica Replica { get; } = new CanonicalWorldReplica();
        public bool ClientStarted { get; private set; }

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
            // Boot survives Stop; each server run gets fresh registries before sibling
            // World components and spawned combatants subscribe to this gateway.
            Gateway = new ServerCombatGateway();
            gluttonySession = null;
            musicSession = null;
            allureSession = null;
            PrototypesEnabled = true;
            nextServerTick = NetworkTime.time;
        }

        public override void OnStopServer()
        {
            // Preserve the gateway object while the remaining Stop callbacks unsubscribe.
            Gateway.Statuses.Clear();
            base.OnStopServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Boot's scene identity survives Stop and is spawned again. Mirror reuses
            // netIds in a new session, so an older death/version must not mask its baseline.
            Replica.Clear();
            ClientStarted = true;
            ClearEnemyHitPresentations();
        }

        public override void OnStopClient()
        {
            ClientStarted = false;
            ClearEnemyHitPresentations();
            // Host can receive a queued death after the enemy's OnStopClient forgot it.
            // Clear the whole session, including such despawned entities and status bindings.
            Replica.Clear();
            base.OnStopClient();
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
            if (NetworkServer.spawned.TryGetValue(entityId, out var identity) && identity != null &&
                identity.TryGetComponent<NetworkEnemySimulationAgent>(out var referenceEnemy) && referenceEnemy.Birth.Enabled &&
                maximumHealth != referenceEnemy.Birth.Health)
                throw new InvalidOperationException("Reference enemy must register its birth maximum health on the first canonical update.");
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

            TargetApplyCanonical(connection, Gateway.CreateSnapshot(), CurrentRound);
        }

        [Server]
        public bool SetPlayerUpgradeSelectionState(uint playerId, bool value)
        {
            bool previous = Gateway.Ledger.IsPlayerSelectingUpgrade(playerId);
            if (!Gateway.Ledger.SetPlayerUpgradeSelectionState(playerId, value))
                return false;
            if (previous != value &&
                Gateway.Ledger.TryGetState(playerId, out CanonicalEntityState state))
                Broadcast(Gateway.CreateEntityUpdate(state));
            return true;
        }

        [Server]
        public EnemyDeathReceipt[] ProcessSubmission(uint senderPlayerId, CombatSubmissionBatch batch)
        {
            Gateway.Round = CurrentRound;
            var canonical = Gateway.ProcessBatch(senderPlayerId, batch, NetworkTime.time, out var receipts);
            Broadcast(canonical);
            return receipts;
        }

        [Server]
        public void SetPlayerUltimateInvulnerable(uint playerId, bool value)
        {
            if (!Gateway.Ledger.TryGetState(playerId, out var previous) ||
                !Gateway.Ledger.SetPlayerUltimateInvulnerable(playerId, value)) return;
            if (Gateway.Ledger.TryGetState(playerId, out var current) && current.StateVersion != previous.StateVersion)
                Broadcast(Gateway.CreateEntityUpdate(current));
        }

        [Server]
        public void SetPlayerTrapInvulnerable(uint playerId, bool value)
        {
            if (!Gateway.Ledger.TryGetState(playerId, out var previous) ||
                !Gateway.Ledger.SetPlayerTrapInvulnerable(playerId, value)) return;
            if (Gateway.Ledger.TryGetState(playerId, out var current) && current.StateVersion != previous.StateVersion)
                Broadcast(Gateway.CreateEntityUpdate(current));
        }

        [Server]
        public void HandleSourceDisconnected(uint sourcePlayerId)
        {
            Broadcast(Gateway.HandleSourceDisconnected(sourcePlayerId, NetworkTime.time));
        }

        [Server]
        public CanonicalEntityState RestorePlayerState(uint entityId, PlayerRuntimeCheckpoint checkpoint)
        {
            CanonicalEntityState state = Gateway.Ledger.RestoreEntityState(entityId, checkpoint.Health);
            Gateway.Statuses.RestoreTarget(checkpoint.PreviousAvatarId, entityId, checkpoint.Statuses, NetworkTime.time);
            Broadcast(Gateway.CreateSnapshot());
            return state;
        }

        [Server]
        public void UnregisterEntity(uint entityId) => Broadcast(Gateway.UnregisterEntity(entityId));

        [Server]
        internal void ResetReferenceEnemy(NetworkEnemySimulationAgent enemy)
        {
            if (enemy == null || !enemy.Birth.Enabled || !enemy.Birth.ResetOnReposition) return;
            Broadcast(Gateway.ResetEnemyCondition(enemy.netId));
            enemy.ResetReferenceCondition();
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

            CaptureEnemyHitPositions(batch.EnemyHitPresentations);
            if (CombatEvidence.Enabled) CombatEvidence.Event("Server", "network.canonical", "Sent", "Broadcast", input: batch, server: batch.ServerSequence);
            RpcApplyCanonical(batch, CurrentRound);
            ServerCanonicalBatchProduced?.Invoke(batch);
        }

        [ClientRpc]
        private void RpcApplyCanonical(CanonicalWorldBatch batch, uint round)
        {
            ApplyCanonicalForRound(batch, round);
        }

        [TargetRpc]
        private void TargetApplyCanonical(
            NetworkConnectionToClient target,
            CanonicalWorldBatch batch, uint round)
        {
            ApplyCanonicalForRound(batch, round);
        }

        private void ApplyCanonicalForRound(CanonicalWorldBatch batch, uint round)
        {
            if (CombatEvidence.Enabled) CombatEvidence.Event("Replica", "network.canonical", round == CurrentRound ? "Received" : "Ignored",
                round == CurrentRound ? null : "WrongRound", input: new { incomingRound = round, batch = Diagnostics.DiagnosticPayload.Freeze(batch) }, server: batch.ServerSequence);
            if (round == CurrentRound) ApplyCanonical(batch);
        }

        private void ApplyCanonical(CanonicalWorldBatch batch)
        {
            // Present live damage before applying a lethal state can disable the actor.
            PresentConfirmedEnemyHits(batch.EnemyHitPresentations);
            Replica.Apply(batch);
            CanonicalBatchReceived?.Invoke(batch);
            // Host may already have despawned these Enemies before this queued RPC.
            // Notify first: Debug owns its short-lived death rows independently.
            if (batch.Entities == null) return;
            foreach (var state in batch.Entities)
                if (state.Kind == (byte)CombatEntityKind.Enemy && !state.Alive &&
                    !NetworkClient.spawned.ContainsKey(state.EntityId))
                    Replica.ForgetEntity(state.EntityId);
        }

        private static bool IsEmpty(CanonicalWorldBatch batch)
        {
            return (batch.Entities == null || batch.Entities.Length == 0) &&
                (batch.Statuses == null || batch.Statuses.Length == 0) &&
                (batch.ConfirmedKills == null || batch.ConfirmedKills.Length == 0) &&
                (batch.EnemyHitPresentations == null || batch.EnemyHitPresentations.Length == 0);
        }

        internal static uint CurrentRound => NetworkManager.singleton is BootGameplayNetworkManager manager && manager.UsePreparationRoom
            ? (NetworkServer.active ? manager.Session.Round : manager.RoomSnapshot.Round) : 0;
        public void ResetClientRound()
        {
            Replica.Clear(); ClearEnemyHitPresentations();
        }
        [Server]
        public void ResetServerRound()
        {
            Gateway.ResetForNextRun(); nextServerTick = NetworkTime.time;
            PrototypesEnabled = true;
        }

        private void OnDestroy()
        {
            ClearEnemyHitPresentations();
            EnemyHitPresented = null;
            EnemyDamageNumberPresented = null;
            Replica.Clear();
            ServerCanonicalBatchProduced = null;
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
