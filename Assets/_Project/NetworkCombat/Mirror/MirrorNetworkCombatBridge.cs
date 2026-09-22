using System;
using Mirror;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class MirrorNetworkCombatBridge : NetworkBehaviour
    {
        [SerializeField, Min(0.01f)] private float flushInterval = 0.05f;
        [SerializeField] private bool enableCombatTrace = true;
        [SerializeField, Min(64)] private int combatTraceCapacity = 4096;

        [SyncVar] private uint ownerPlayerId;
        [SyncVar] private ushort sourceSlot;
        [SyncVar] private ushort connectionEpoch;

        private SequentialCombatEventIdSource eventIds;
        private ClientCombatCollector collector;
        private float nextFlushTime;
        private uint batchSequence;
        [SyncVar] private uint collectorRound;

        public uint OwnerPlayerId => ownerPlayerId;
        public uint SourceEntityId => netId;
        public ushort ConnectionEpoch => connectionEpoch;
        public ICombatEventIdSource EventIds => eventIds;
        public ClientCombatCollector Collector => collector;
        public CombatTraceRecorder Trace { get; private set; }

        public event Action<ClientCombatCollector, ICombatEventIdSource> OwnerCollectorReady;

        public override void OnStartServer()
        {
            base.OnStartServer();
            ownerPlayerId = netId;
            collectorRound = NetworkCombatWorld.CurrentRound;
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            if (world == null)
            {
                Debug.LogError("NetworkCombatWorld is required before player combat bridges spawn.", this);
                return;
            }

            connectionEpoch = world.AllocateConnectionEpoch();
            sourceSlot = (ushort)(ownerPlayerId & ushort.MaxValue);
            if (sourceSlot == 0)
            {
                sourceSlot = 1;
            }

            world.Gateway.Ledger.RegisterSource(netId, ownerPlayerId);
            world.Gateway.RegisterClientIdentity(ownerPlayerId, sourceSlot, connectionEpoch);
        }

        public override void OnStartAuthority()
        {
            base.OnStartAuthority();
            if (collector != null) return;
            // Rebinding the same avatar must not reuse event sequences in its existing epoch.
            if (eventIds == null) eventIds = new SequentialCombatEventIdSource(sourceSlot, connectionEpoch);
            Trace = enableCombatTrace
                ? new CombatTraceRecorder(combatTraceCapacity)
                : null;
            collector = new ClientCombatCollector(
                ownerPlayerId,
                eventIds,
                trace: Trace, timeSource: () => Time.unscaledTimeAsDouble,
                isClientFinalEnemy: id => NetworkClient.spawned.TryGetValue(id, out var target) &&
                    target.GetComponent<NetworkCombatantAdapter>()?.IsClientFinalEnemy == true);
            collector.DamageResolved += HandleDamageResolved;
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            if (world != null)
            {
                world.Replica.KillConfirmed += HandleConfirmedKill;
            }

            nextFlushTime = Time.unscaledTime + flushInterval;
            OwnerCollectorReady?.Invoke(collector, eventIds);
            // Request after OnStartAuthority so the player SpawnMessage is already
            // known to this client. Sending a TargetRpc from OnStartServer would be
            // queued before Mirror's SpawnMessage for this object.
            CmdRequestCanonicalSnapshot();
        }

        [Command]
        private void CmdRequestCanonicalSnapshot(
            NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender != connectionToClient)
            {
                if (CombatEvidence.Enabled) CombatEvidence.Event("Server", "network.snapshot_request", "Rejected", "ConnectionMismatch");
                return;
            }

            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            if (world != null)
            {
                world.SendSnapshot(sender);
            }
        }

        [ClientCallback]
        private void Update()
        {
            if (!isOwned || collector == null || Time.unscaledTime < nextFlushTime)
            {
                return;
            }

            nextFlushTime = Time.unscaledTime + flushInterval;
            Flush();
        }

        public void ObserveStatus(StatusController controller)
        {
            if (collector == null)
            {
                throw new InvalidOperationException("Owner collector is not ready yet.");
            }

            collector.Observe(controller);
        }

        public void Flush()
        {
            if (!isOwned || collector == null ||
                (collector.PendingResultCount == 0 &&
                 collector.PendingStatusMutationCount == 0 &&
                 collector.PendingPlayerHealthReportCount == 0 &&
                 !collector.HasDueDeaths(Time.unscaledTimeAsDouble)))
            {
                return;
            }

            batchSequence = unchecked(batchSequence + 1u);
            if (batchSequence == 0u)
            {
                batchSequence = 1u;
            }

            CombatSubmissionBatch batch = collector.Drain(batchSequence, now: Time.unscaledTimeAsDouble);
            batch.Round = collectorRound;
            if (CombatEvidence.Enabled) CombatEvidence.Event("Owner", "network.submit", "Sent", "ReliableCommand",
                source: ownerPlayerId, input: batch, batch: batch.BatchSequence);
            CmdSubmit(batch);
        }

        // Reliable batching gives eventual delivery under packet loss while owner-side
        // combat remains immediate and never waits for this command.
        [Command]
        private void CmdSubmit(
            CombatSubmissionBatch batch,
            NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender != connectionToClient)
            {
                if (CombatEvidence.Enabled) CombatEvidence.Event("Server", "network.submit", "Rejected", "ConnectionMismatch", input: batch, batch: batch.BatchSequence);
                return;
            }

            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            if (CombatEvidence.Enabled) CombatEvidence.Event("Server", "network.submit", world != null ? "Received" : "Ignored", world != null ? null : "WorldUnavailable",
                source: ownerPlayerId, input: batch, batch: batch.BatchSequence);
            if (world != null)
            {
                var receipts = world.ProcessSubmission(ownerPlayerId, batch);
                if (receipts.Length > 0) TargetConfirmEnemyDeaths(sender, receipts, batch.Round);
            }
        }

        [TargetRpc]
        private void TargetConfirmEnemyDeaths(NetworkConnectionToClient target, EnemyDeathReceipt[] receipts, uint round)
        {
            if (round != NetworkCombatWorld.CurrentRound || collector == null)
            {
                if (CombatEvidence.Enabled) CombatEvidence.Event("Owner", "death.receipt", "Ignored", collector == null ? "CollectorUnavailable" : "WrongRound", input: new { round, receipts });
                return;
            }
            foreach (var receipt in receipts)
                if (collector.AcknowledgeDeath(receipt, Time.unscaledTimeAsDouble) && receipt.Kill.TargetEntityId != 0)
                    NetworkCombatWorld.Instance?.Replica.Apply(new CanonicalWorldBatch
                    { ConfirmedKills = new[] { receipt.Kill } });
        }

        public override void OnStopAuthority()
        {
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            if (world != null)
            {
                world.Replica.KillConfirmed -= HandleConfirmedKill;
            }

            if (collector != null) collector.DamageResolved -= HandleDamageResolved;
            collector?.Dispose();
            collector = null;
            Trace = null;
            base.OnStopAuthority();
        }

        public override void OnStopClient()
        {
            OnStopAuthority();
            base.OnStopClient();
        }

        public override void OnStopServer()
        {
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            if (world != null && ownerPlayerId != 0)
            {
                world.HandleSourceDisconnected(ownerPlayerId);
                world.Gateway.Ledger.UnregisterSource(netId);
                world.Gateway.UnregisterClientIdentity(ownerPlayerId);
            }

            base.OnStopServer();
        }

        private void HandleDamageResolved(CombatEvent damage)
        {
            if (isOwned)
                NetworkCombatWorld.Instance?.PresentPredictedEnemyHit(damage);
        }

        private void HandleConfirmedKill(ConfirmedKill kill)
        {
            Trace?.RecordConfirmedKill(
                new CombatEventId(kill.CauseEventId),
                kill.KillerPlayerId,
                kill.TargetEntityId,
                kill.TargetStateVersion);
        }
    }
}
