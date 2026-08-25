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

        public uint OwnerPlayerId => ownerPlayerId;
        public uint SourceEntityId => netId;
        public ICombatEventIdSource EventIds => eventIds;
        public ClientCombatCollector Collector => collector;
        public CombatTraceRecorder Trace { get; private set; }

        public event Action<ClientCombatCollector, ICombatEventIdSource> OwnerCollectorReady;

        public override void OnStartServer()
        {
            base.OnStartServer();
            ownerPlayerId = netId;
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
            eventIds = new SequentialCombatEventIdSource(sourceSlot, connectionEpoch);
            Trace = enableCombatTrace
                ? new CombatTraceRecorder(combatTraceCapacity)
                : null;
            collector = new ClientCombatCollector(
                ownerPlayerId,
                eventIds,
                trace: Trace);
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
                 collector.PendingPlayerHealthReportCount == 0))
            {
                return;
            }

            batchSequence = unchecked(batchSequence + 1u);
            if (batchSequence == 0u)
            {
                batchSequence = 1u;
            }

            CombatSubmissionBatch batch = collector.Drain(batchSequence);
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
                return;
            }

            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            if (world != null)
            {
                world.ProcessSubmission(ownerPlayerId, batch);
            }
        }

        public override void OnStopAuthority()
        {
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            if (world != null)
            {
                world.Replica.KillConfirmed -= HandleConfirmedKill;
            }

            collector?.Dispose();
            collector = null;
            eventIds = null;
            Trace = null;
            base.OnStopAuthority();
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
