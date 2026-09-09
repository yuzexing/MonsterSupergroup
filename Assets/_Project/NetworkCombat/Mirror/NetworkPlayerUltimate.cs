using System;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Charge/use authority and presentation transport. The actual attack uses the existing GAS runtime.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerBuildRuntime), typeof(MirrorNetworkCombatBridge))]
    public sealed class NetworkPlayerUltimate : NetworkBehaviour
    {
        [SerializeField] private UltimateData ultimateData;
        [SyncVar(hook = nameof(OnStateChanged))] private NetworkUltimateState state;
        [SyncVar] private bool hasBaseline;
        [SyncVar] private bool serverExecutionEnabled = true;
        private readonly PlayerUltimateRuntime serverRuntime = new PlayerUltimateRuntime();
        private readonly PlayerUltimateRuntime ownerView = new PlayerUltimateRuntime();
        private PlayerUltimateSnapshot? preparedState;
        private PlayerMovement player;
        private PlayerBuildRuntime build;
        private MirrorNetworkCombatBridge bridge;
        private NetworkModifierSelection selection;
        private CombatantBehaviour combatant;
        private DanteUltimateAttack ownedAttack, remoteAttack;
        private WeaponRuntimeBehaviour ownedRuntime;
        private RuntimeEquipmentModifiers intrinsicModifiers;
        private ulong pendingUseId, serverRootId;
        private uint lastServerUseSequence, lastServerVisualSequence, lastRemoteVisualSequence;
        private ulong suspendedRemoteRootId;
        private uint lastOwnerStateRevision;
        private bool ownerServerExecutionSuspended;
        private NetworkConnectionToClient registeredServerOwner;
        private NetworkUltimatePresentationSpawn? serverPresentation;
        public UltimateData Definition => ultimateData;
        public DanteUltimateAttack OwnerAttack => ownedAttack;
        public bool HasCharge => isOwned && hasBaseline && ownerView.HasCharge;
        public int AcceptedUseCount { get; private set; }
        public int RejectedUseCount { get; private set; }
        public int ReplicaSpawnCount { get; private set; }
        public int ReplicaTerminationCount { get; private set; }
        public bool HasRemotePresentation => remoteAttack != null && remoteAttack.IsPresentationActive;

        private void Awake()
        {
            player = GetComponent<PlayerMovement>(); build = GetComponent<PlayerBuildRuntime>();
            bridge = GetComponent<MirrorNetworkCombatBridge>(); selection = GetComponent<NetworkModifierSelection>();
            combatant = GetComponent<CombatantBehaviour>();
        }

        public void PrepareServerRestore(PlayerUltimateSnapshot snapshot)
        {
            if (netId != 0) throw new InvalidOperationException("Prepare Ultimate restoration before spawning the avatar.");
            var validator = new PlayerUltimateRuntime(); validator.Restore(snapshot);
            preparedState = snapshot;
        }
        public PlayerUltimateSnapshot CaptureServerState() => serverRuntime.Capture();

        public override void OnStartServer()
        {
            serverExecutionEnabled = true;
            serverRuntime.Restore(preparedState ?? default);
            preparedState = null;
            serverRootId = 0; serverPresentation = null;
            lastServerUseSequence = lastServerVisualSequence = 0;
            registeredServerOwner = connectionToClient;
            PublishState(); hasBaseline = true;
            ApplyServerProtection();
        }

        public override void OnStartClient()
        {
            if (!isOwned) CmdRequestCurrentPresentation();
        }

        public override void OnStartAuthority()
        {
            lastOwnerStateRevision = 0;
            ownerServerExecutionSuspended = !serverExecutionEnabled;
            ApplyOwnerState(state);
            DisposeRemote();
            player.BindUltimateInput(RequestUse, () => HasCharge);
        }

        private void OnEnable()
        {
            if (NetworkServer.active && isServer && netId != 0 && !serverExecutionEnabled)
            {
                serverExecutionEnabled = true;
                ApplyServerProtection();
                if (connectionToClient != null)
                    TargetServerExecutionState(connectionToClient, true, state);
            }
            if (!NetworkClient.active || netId == 0 || !isClient) return;
            if (isOwned) player.BindUltimateInput(RequestUse, () => HasCharge);
            else CmdRequestCurrentPresentation();
        }

        [Server]
        public bool ServerGrantCharge()
        {
            if (ultimateData == null || !serverRuntime.TryGrantCharge()) return false;
            PublishState();
            return true;
        }

        private void OnStateChanged(NetworkUltimateState previous, NetworkUltimateState current) => ApplyOwnerState(current);

        private void PublishState()
        {
            uint revision = unchecked(state.Revision + 1u);
            state = new NetworkUltimateState { Revision = revision == 0 ? 1u : revision, Snapshot = serverRuntime.Capture() };
            ApplyOwnerState(state);
        }

        private void ApplyOwnerState(NetworkUltimateState current)
        {
            if (!isOwned || !ProjectilePresentationSequence.IsNewer(current.Revision, lastOwnerStateRevision)) return;
            ownerView.Restore(current.Snapshot);
            lastOwnerStateRevision = current.Revision;
        }

        private void Update()
        {
            if (isServer)
            {
                if (registeredServerOwner != connectionToClient)
                {
                    if (serverRootId != 0) NetworkCombatWorld.Instance?.Gateway.Attacks.Retire(netId, serverRootId);
                    if (serverPresentation.HasValue) RpcEndPresentation(serverPresentation.Value.AttackEventId);
                    serverRootId = 0; serverPresentation = null;
                    registeredServerOwner = connectionToClient;
                }
                ApplyServerProtection();
            }
            if (!isOwned || !NetworkClient.active) return;
            if (hasBaseline) combatant.SetUltimateInvulnerable(!ownerServerExecutionSuspended && ownerView.IsInvulnerable(NetworkTime.time));
            if (ownedAttack == null) TryInitializeOwnerAttack();
            if ((!build.IsBuildActive || player.IsUpgradeSelectionLocked || !combatant.IsAlive) && ownedAttack != null)
                ownedAttack.Cancel(ownedAttack.ActiveUseId);
        }

        private void ApplyServerProtection()
        {
            NetworkCombatWorld.Instance?.SetPlayerUltimateInvulnerable(netId,
                serverExecutionEnabled && serverRuntime.IsInvulnerable(NetworkTime.time));
        }

        public bool RequestUse()
        {
            if (!isActiveAndEnabled || !isOwned || !NetworkClient.active || !hasBaseline || ownerServerExecutionSuspended || pendingUseId != 0 ||
                !player.isActiveAndEnabled || !player.IsRuntimeInitialized || !player.IsLocalOwnerBound ||
                !build.IsBuildActive || !selection.HasOwnerBaseline || player.IsUpgradeSelectionLocked || !combatant.IsAlive ||
                (ownedAttack != null && ownedAttack.IsNativeActive) ||
                !ownerView.CanUse(NetworkTime.time) || !TryInitializeOwnerAttack()) return false;
            ulong id = bridge.EventIds.Next().Value;
            pendingUseId = id;
            CmdUseUltimate(id, selection.OwnerBuildRevision);
            return true;
        }

        [Command(channel = Channels.Reliable)]
        private void CmdUseUltimate(ulong rootId, uint buildRevision, NetworkConnectionToClient sender = null)
        {
            var world = NetworkCombatWorld.Instance;
            var id = new CombatEventId(rootId);
            var source = ultimateData != null ? ultimateData.ultimateAttackWeaponBehaviour as DanteUltimateAttack : null;
            if (!isActiveAndEnabled || !serverExecutionEnabled || sender == null || sender != connectionToClient || world == null || source == null ||
                id.Sequence <= lastServerUseSequence || !world.Gateway.ClientIdentities.Validate(netId, rootId, id.Sequence) ||
                !build.IsBuildActive || buildRevision == 0 || buildRevision != selection.BuildRevision ||
                !world.Gateway.Ledger.IsAlive(netId) || world.Gateway.Ledger.IsPlayerSelectingUpgrade(netId) ||
                serverRootId != 0 || !serverRuntime.CanUse(NetworkTime.time))
            { RejectUse(sender, rootId); return; }
            uint abilityId = UltimateNativeDefinitionAdapter.EncodeAbilityId(ultimateData.Id);
            if (world.Gateway.Attacks.Admit(netId, bridge.SourceEntityId, abilityId, buildRevision, rootId) != CombatRejectionReason.None)
            { RejectUse(sender, rootId); return; }
            serverRuntime.TryConsume(NetworkTime.time, source.SequenceDuration, source.InvulnerabilityDuration);
            PublishState(); serverRootId = rootId; lastServerUseSequence = id.Sequence;
            AcceptedUseCount++;
            ApplyServerProtection();
            // The world sends the trusted pulse only to each enemy's assigned simulator.
            NetworkEnemySimulationWorld.Instance?.ServerApplyUltimateKnockback(netId, rootId, abilityId,
                player.transform.position, source.KnockbackRadius, source.InitialKnockbackSettings);
            TargetUseResult(sender, rootId, true, state);
        }

        private void RejectUse(NetworkConnectionToClient sender, ulong rootId)
        {
            RejectedUseCount++;
            if (sender == connectionToClient && sender != null) TargetUseResult(sender, rootId, false, state);
        }

        [TargetRpc(channel = Channels.Reliable)]
        private void TargetUseResult(NetworkConnectionToClient target, ulong rootId, bool accepted, NetworkUltimateState snapshot)
        {
            if (!isOwned) return;
            ApplyOwnerState(snapshot);
            if (pendingUseId != rootId)
            {
                // A component can be disabled after server acceptance but before this acknowledgement.
                if (accepted && (ownedAttack == null || ownedAttack.ActiveUseId != rootId)) CmdCompleteUltimate(rootId);
                return;
            }
            pendingUseId = 0;
            if (!accepted) return;
            combatant.SetUltimateInvulnerable(!ownerServerExecutionSuspended && ownerView.IsInvulnerable(NetworkTime.time));
            if (!isActiveAndEnabled || ownerServerExecutionSuspended || !TryInitializeOwnerAttack() || !ownedAttack.TryBegin(rootId))
                CmdCompleteUltimate(rootId);
        }

        [TargetRpc(channel = Channels.Reliable)]
        private void TargetServerExecutionState(NetworkConnectionToClient target, bool executionEnabled, NetworkUltimateState snapshot)
        {
            if (!isOwned) return;
            ApplyOwnerState(snapshot);
            ownerServerExecutionSuspended = !executionEnabled;
            if (!executionEnabled)
            {
                pendingUseId = 0;
                if (ownedAttack != null) ownedAttack.Cancel(ownedAttack.ActiveUseId);
            }
            combatant.SetUltimateInvulnerable(executionEnabled && ownerView.IsInvulnerable(NetworkTime.time));
        }

        private void CancelServerExecution(bool notifyClients)
        {
            bool wasEnabled = serverExecutionEnabled;
            serverExecutionEnabled = false;
            ulong cancelledRoot = serverRootId;
            serverRootId = 0;
            serverPresentation = null;
            var world = NetworkCombatWorld.Instance;
            if (cancelledRoot != 0) world?.Gateway.Attacks.Retire(netId, cancelledRoot);
            world?.SetPlayerUltimateInvulnerable(netId, false);
            // Runtime deadlines and any independently held charge remain authoritative and unchanged.
            if (!notifyClients || !NetworkServer.active || !wasEnabled) return;
            if (connectionToClient != null) TargetServerExecutionState(connectionToClient, false, state);
            if (cancelledRoot != 0) RpcEndPresentation(cancelledRoot);
        }

        private bool TryInitializeOwnerAttack()
        {
            if (ownedAttack != null) return true;
            var servicesProvider = GetComponent<CombatRuntimeServiceProvider>();
            if (!isOwned || !isActiveAndEnabled || ultimateData == null || !hasBaseline || !build.IsBuildActive ||
                !selection.HasOwnerBaseline || bridge.Collector == null || bridge.EventIds == null || !servicesProvider.IsNetworkConfigured) return false;
            var source = ultimateData.ultimateAttackWeaponBehaviour as DanteUltimateAttack;
            if (source == null) return false;
            var staging = new GameObject("Ultimate Owner Initialization"); staging.SetActive(false);
            try
            {
                ownedAttack = Instantiate(source, staging.transform);
                ownedAttack.gameObject.SetActive(false);
                ownedAttack.transform.SetParent(player.AttacksParent != null ? player.AttacksParent : player.transform, false);
                ownedRuntime = ownedAttack.gameObject.AddComponent<WeaponRuntimeBehaviour>();
                ownedRuntime.InitializeOnAwake = false;
                var services = servicesProvider.Services;
                services.Configure(ownedRuntime);
                intrinsicModifiers = UltimateNativeDefinitionAdapter.CreateIntrinsicModifiers(ultimateData);
                ownedRuntime.InitializeExternal(UltimateNativeDefinitionAdapter.ToNativeBaseStats(ultimateData),
                    UltimateNativeDefinitionAdapter.EncodeAbilityId(ultimateData.Id), intrinsicModifiers, build.PerkMultipliers,
                    null, services.EventIds, services.EventSink, services.TriggerGuard, services.TimeSource);
                ownedAttack.ConfigureNativeUltimate(player, ownedRuntime, ultimateData);
                ownedAttack.PresentationSpawned += HandleSpawn;
                ownedAttack.PresentationTerminated += HandleTermination;
                ownedAttack.NativeAttackCompleted += HandleCompleted;
                // The original Ultimate root is hidden until an admitted use activates StartPlayback.
                return true;
            }
            catch { DisposeOwner(); throw; }
            finally { Destroy(staging); }
        }

        private void HandleSpawn(UltimatePresentationSpawn spawn)
        {
            if (isOwned && NetworkClient.active) CmdSubmitPresentation(new NetworkUltimatePresentationSpawn
            { SourcePlayerId = netId, UltimateId = spawn.UltimateId, AttackEventId = spawn.AttackEventId,
                EventNetworkTime = NetworkTime.time, Stats = spawn.Stats, Element = spawn.Element });
        }

        private void HandleTermination(UltimatePresentationTermination termination)
        { if (isOwned && NetworkClient.active) CmdEndPresentation(termination.AttackEventId); }

        private void HandleCompleted(ulong rootId)
        {
            if (!isOwned || !NetworkClient.active) return;
            while (bridge.Collector != null && (bridge.Collector.PendingResultCount > 0 ||
                bridge.Collector.PendingStatusMutationCount > 0 || bridge.Collector.PendingPlayerHealthReportCount > 0)) bridge.Flush();
            CmdCompleteUltimate(rootId);
        }

        [Command(channel = Channels.Reliable)]
        private void CmdSubmitPresentation(NetworkUltimatePresentationSpawn spawn, NetworkConnectionToClient sender = null)
        {
            uint sequence = new CombatEventId(spawn.AttackEventId).Sequence;
            if (!isActiveAndEnabled || !serverExecutionEnabled || sender != connectionToClient || sender == null || ultimateData == null || !spawn.IsValid || spawn.SourcePlayerId != netId ||
                spawn.UltimateId != ultimateData.Id || spawn.AttackEventId != serverRootId || sequence <= lastServerVisualSequence ||
                spawn.EventNetworkTime > NetworkTime.time + .1d || spawn.EventNetworkTime < NetworkTime.time - 2d ||
                NetworkCombatWorld.Instance?.Gateway.Attacks.Contains(netId, spawn.AttackEventId,
                    UltimateNativeDefinitionAdapter.EncodeAbilityId(ultimateData.Id)) != true) return;
            lastServerVisualSequence = sequence; serverPresentation = spawn;
            RpcStartPresentation(spawn);
        }

        [Command(channel = Channels.Reliable)]
        private void CmdEndPresentation(ulong rootId, NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender != connectionToClient || serverPresentation?.AttackEventId != rootId) return;
            serverPresentation = null;
            RpcEndPresentation(rootId);
        }

        [Command(channel = Channels.Reliable)]
        private void CmdCompleteUltimate(ulong rootId, NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender != connectionToClient || rootId != serverRootId) return;
            if (serverPresentation.HasValue) { serverPresentation = null; RpcEndPresentation(rootId); }
            NetworkCombatWorld.Instance?.Gateway.Attacks.Retire(netId, rootId);
            serverRootId = 0;
        }

        [ClientRpc(channel = Channels.Reliable)]
        private void RpcStartPresentation(NetworkUltimatePresentationSpawn spawn)
        { if (!isOwned) ApplyRemotePresentation(spawn); }

        [ClientRpc(channel = Channels.Reliable)]
        private void RpcEndPresentation(ulong rootId)
        {
            if (!isOwned && suspendedRemoteRootId == rootId) suspendedRemoteRootId = 0;
            if (!isOwned && remoteAttack != null && remoteAttack.ActiveUseId == rootId)
            { remoteAttack.Cancel(rootId); ReplicaTerminationCount++; }
        }

        [Command(requiresAuthority = false, channel = Channels.Reliable)]
        private void CmdRequestCurrentPresentation(NetworkConnectionToClient sender = null)
        {
            if (sender != null && sender.isAuthenticated && sender.identity != null && serverPresentation.HasValue)
                TargetCurrentPresentation(sender, serverPresentation.Value);
        }

        [TargetRpc(channel = Channels.Reliable)]
        private void TargetCurrentPresentation(NetworkConnectionToClient target, NetworkUltimatePresentationSpawn spawn)
        { if (!isOwned) ApplyRemotePresentation(spawn); }

        private void ApplyRemotePresentation(NetworkUltimatePresentationSpawn spawn)
        {
            uint sequence = new CombatEventId(spawn.AttackEventId).Sequence;
            bool resuming = remoteAttack == null && suspendedRemoteRootId == spawn.AttackEventId &&
                sequence == lastRemoteVisualSequence;
            if (!isActiveAndEnabled || !spawn.IsValid || spawn.SourcePlayerId != netId || ultimateData == null ||
                spawn.UltimateId != ultimateData.Id || (sequence <= lastRemoteVisualSequence && !resuming)) return;
            float age = (float)Math.Max(0d, NetworkTime.time - spawn.EventNetworkTime);
            var source = ultimateData.ultimateAttackWeaponBehaviour as DanteUltimateAttack;
            if (source == null || age >= source.SequenceDuration)
            {
                if (resuming) suspendedRemoteRootId = 0;
                return;
            }
            if (remoteAttack == null)
            {
                var staging = new GameObject("Ultimate Replica Initialization"); staging.SetActive(false);
                try
                {
                    remoteAttack = Instantiate(source, staging.transform);
                    remoteAttack.gameObject.SetActive(false);
                    remoteAttack.transform.SetParent(player.AttacksParent != null ? player.AttacksParent : player.transform, false);
                    remoteAttack.InitializePresentationReplica(player);
                }
                finally { Destroy(staging); }
            }
            if (remoteAttack.PlayPresentation(spawn.ToSpawn(), age))
            { lastRemoteVisualSequence = sequence; suspendedRemoteRootId = 0; ReplicaSpawnCount++; }
        }

        private void DisposeOwner()
        {
            pendingUseId = 0;
            player?.UnbindUltimateInput();
            combatant?.SetUltimateInvulnerable(false);
            if (ownedAttack != null)
            {
                ownedAttack.DisposeNativeUltimate();
                ownedAttack.PresentationSpawned -= HandleSpawn;
                ownedAttack.PresentationTerminated -= HandleTermination;
                ownedAttack.NativeAttackCompleted -= HandleCompleted;
                Destroy(ownedAttack.gameObject);
            }
            ownedAttack = null;
            ownedRuntime?.Shutdown(); ownedRuntime = null;
            intrinsicModifiers?.Clear(); intrinsicModifiers = null;
        }
        private void DisposeRemote(bool preserveActiveRootForResume = false)
        {
            // A local disable destroys visuals, not the server's active use. Only that exact
            // root may be rebuilt from a fresh server response; ordinary duplicates stay rejected.
            if (!preserveActiveRootForResume) suspendedRemoteRootId = 0;
            else if (remoteAttack != null)
                suspendedRemoteRootId = remoteAttack.IsPresentationActive ? remoteAttack.ActiveUseId : 0;
            if (remoteAttack != null) { remoteAttack.DisposePresentationReplica(); Destroy(remoteAttack.gameObject); }
            remoteAttack = null;
        }
        private void OnDisable()
        {
            // Retire on the server before local Owner disposal; a Host callback cannot supply this for a dedicated server.
            if (isServer) CancelServerExecution(true);
            DisposeOwner(); DisposeRemote(preserveActiveRootForResume: true);
        }
        private void OnDestroy() { DisposeOwner(); DisposeRemote(); }
        public override void OnStopAuthority() => DisposeOwner();
        public override void OnStopClient() { DisposeOwner(); DisposeRemote(); lastRemoteVisualSequence = 0; }
        public override void OnStopServer()
        {
            CancelServerExecution(false);
        }
    }
}
