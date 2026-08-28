using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(CombatantBehaviour))]
    public sealed class NetworkCombatantAdapter : NetworkBehaviour
    {
        [SerializeField] private CombatantBehaviour combatant;
        [SerializeField] private MirrorNetworkCombatBridge ownerBridge;
        [SerializeField] private CombatEntityKind entityKind = CombatEntityKind.Enemy;
        [SerializeField] private CombatEntityAuthority authority =
            CombatEntityAuthority.ServerCanonical;

        private bool statusObserved;
        private uint localPlayerId;
        private uint targetOwnerPlayerId;
        private uint ownerReportVersion;
        private bool applyingCanonical;
        private ICombatEventSink localStatusEventSink;

        public void Configure(
            CombatantBehaviour targetCombatant,
            CombatEntityKind kind,
            CombatEntityAuthority entityAuthority)
        {
            combatant = targetCombatant != null
                ? targetCombatant
                : throw new System.ArgumentNullException(nameof(targetCombatant));
            entityKind = kind;
            authority = entityAuthority;
        }

        private void Awake()
        {
            if (combatant == null)
            {
                combatant = GetComponent<CombatantBehaviour>();
            }

            if (ownerBridge == null)
            {
                ownerBridge = GetComponent<MirrorNetworkCombatBridge>();
            }

            combatant.ConfigureKillConfirmation(true);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            if (world == null)
            {
                Debug.LogError("NetworkCombatWorld must exist before combatants spawn.", this);
                return;
            }

            uint owner = authority == CombatEntityAuthority.OwnerFinal && connectionToClient != null
                ? netId
                : 0u;
            combatant.ConfigureEntityId(netId);
            targetOwnerPlayerId = owner;
            combatant.ConfigureStatusExecution(
                new StatusExecutionScope(false, true, 0, targetOwnerPlayerId));
            combatant.ConfigureCanonicalConsequenceExecution(true);
            world.Gateway.ConfirmedKillProduced += HandleConfirmedKill;
            world.ServerCanonicalBatchProduced += HandleServerCanonicalBatch;
            world.RegisterEntity(netId, combatant.MaxHealth, entityKind, authority, owner);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            combatant.ConfigureEntityId(netId);
            combatant.ConfigureCanonicalConsequenceExecution(NetworkServer.active);
            combatant.ConfigureStatusExecution(new StatusExecutionScope(
                false,
                NetworkServer.active,
                0,
                targetOwnerPlayerId));
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            if (world == null)
            {
                return;
            }

            world.Replica.RegisterStatusController(netId, combatant.StatusController);
            world.Replica.EntityChanged += HandleCanonicalEntityChanged;
            world.Replica.KillConfirmed += HandleConfirmedKill;
            if (world.Replica.TryGetEntity(netId, out CanonicalEntityState current))
            {
                HandleCanonicalEntityChanged(current);
            }

            TryObserveWithLocalCollector();
        }

        public override void OnStartAuthority()
        {
            base.OnStartAuthority();
            if (authority != CombatEntityAuthority.OwnerFinal)
            {
                return;
            }

            ownerReportVersion = combatant.StateVersion > 0u
                ? combatant.StateVersion
                : 1u;
            combatant.HealthChanged += HandleOwnerHealthChanged;
        }

        public override void OnStopAuthority()
        {
            if (combatant != null)
            {
                combatant.HealthChanged -= HandleOwnerHealthChanged;
            }

            base.OnStopAuthority();
        }

        [ClientCallback]
        private void Update()
        {
            if (!statusObserved)
            {
                TryObserveWithLocalCollector();
            }
        }

        public override void OnStopClient()
        {
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            if (world != null && combatant != null)
            {
                world.Replica.UnregisterStatusController(netId, combatant.StatusController);
                world.Replica.EntityChanged -= HandleCanonicalEntityChanged;
                world.Replica.KillConfirmed -= HandleConfirmedKill;
            }

            combatant?.ClearStatusCombatEvents(localStatusEventSink);
            localStatusEventSink = null;

            base.OnStopClient();
        }

        public override void OnStopServer()
        {
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            if (world != null)
            {
                world.Gateway.ConfirmedKillProduced -= HandleConfirmedKill;
                world.ServerCanonicalBatchProduced -= HandleServerCanonicalBatch;
                world.Gateway.Statuses.RemoveTarget(netId);
                world.Gateway.Ledger.UnregisterEntity(netId);
            }

            base.OnStopServer();
        }

        private void TryObserveWithLocalCollector()
        {
            MirrorNetworkCombatBridge[] bridges =
                FindObjectsByType<MirrorNetworkCombatBridge>(FindObjectsSortMode.None);
            for (int i = 0; i < bridges.Length; i++)
            {
                if (!bridges[i].isOwned || bridges[i].Collector == null)
                {
                    continue;
                }

                bridges[i].ObserveStatus(combatant.StatusController);
                combatant.ConfigureStatusInstanceIds(
                    new CombatEventStatusInstanceIdSource(bridges[i].EventIds));
                if (authority == CombatEntityAuthority.ServerCanonical)
                {
                    localStatusEventSink = bridges[i].Collector;
                    combatant.ConfigureStatusCombatEvents(
                        bridges[i].EventIds,
                        localStatusEventSink);
                }
                localPlayerId = bridges[i].OwnerPlayerId;
                combatant.ConfigureStatusExecution(new StatusExecutionScope(
                    false,
                    NetworkServer.active,
                    localPlayerId,
                    targetOwnerPlayerId));
                statusObserved = true;
                return;
            }
        }

        private void HandleCanonicalEntityChanged(CanonicalEntityState state)
        {
            if (state.EntityId == netId)
            {
                targetOwnerPlayerId = state.OwnerPlayerId;
                combatant.ConfigureStatusExecution(new StatusExecutionScope(
                    false,
                    NetworkServer.active,
                    localPlayerId,
                    targetOwnerPlayerId));
                applyingCanonical = true;
                try
                {
                    combatant.ApplyCanonicalHealth(
                        state.Health,
                        state.MaxHealth,
                        state.StateVersion);
                    if (state.StateVersion > ownerReportVersion)
                    {
                        ownerReportVersion = state.StateVersion;
                    }
                }
                finally
                {
                    applyingCanonical = false;
                }
            }
        }

        private void HandleOwnerHealthChanged(int current, int maximum)
        {
            if (applyingCanonical || !isOwned ||
                authority != CombatEntityAuthority.OwnerFinal ||
                ownerBridge == null || ownerBridge.Collector == null ||
                ownerBridge.EventIds == null)
            {
                return;
            }

            ownerReportVersion = unchecked(ownerReportVersion + 1u);
            if (ownerReportVersion == 0u)
            {
                ownerReportVersion = 1u;
            }

            CombatEventId eventId = ownerBridge.EventIds.Next();
            ownerBridge.Collector.EnqueuePlayerHealth(new PlayerHealthReport
            {
                EventId = eventId.Value,
                Sequence = eventId.Sequence,
                PlayerId = ownerBridge.OwnerPlayerId,
                EntityId = netId,
                Health = current,
                MaxHealth = maximum,
                Alive = current > 0,
                StateVersion = ownerReportVersion
            });
        }

        private void HandleConfirmedKill(ConfirmedKill kill)
        {
            if (kill.TargetEntityId == netId)
            {
                combatant.ReceiveConfirmedKill(kill);
            }
        }

        private void HandleServerCanonicalBatch(CanonicalWorldBatch batch)
        {
            // A host receives the same state through its local ClientRpc replica.
            // A dedicated server has no client replica, so apply its ledger output
            // directly to the scene-side Combatant read model.
            if (NetworkClient.active || batch.Entities == null)
            {
                return;
            }

            for (int i = 0; i < batch.Entities.Length; i++)
            {
                if (batch.Entities[i].EntityId == netId)
                {
                    HandleCanonicalEntityChanged(batch.Entities[i]);
                    return;
                }
            }
        }
    }
}
