using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [Serializable]
    public struct PrototypeSelectionState
    {
        public PrototypeAbilityId Ability;
        public uint Revision;
        public bool Enabled;
    }

    [DisallowMultipleComponent]
    public sealed class NetworkPlayerPrototypeAbilities : NetworkBehaviour
    {
        [SyncVar] private PrototypeSelectionState state;
        private PrototypeSelectionState receipt;
        private readonly Dictionary<PrototypeAbilityId, IPrototypeAbilityModule> modules =
            new Dictionary<PrototypeAbilityId, IPrototypeAbilityModule>();
        private PlayerMovement player;
        private PlayerBuildRuntime build;
        private NetworkModifierSelection selection;
        private MirrorNetworkCombatBridge bridge;
        private CombatantBehaviour combatant;
        private ModifierSelectionController selectionPresentation;
        private ulong pendingSelection;
        private uint lastRequestSequence;
        private bool wasAlive;
        private NetworkConnectionToClient serverOwner;

        private PrototypeSelectionState EffectiveState => isOwned && GluttonyReplica.IsNewer(receipt.Revision, state.Revision) ? receipt : state;
        public PrototypeAbilityId SelectedAbility => EffectiveState.Ability;
        public uint SelectionRevision => EffectiveState.Revision;
        public bool PrototypeEnabled => EffectiveState.Revision != 0 && EffectiveState.Enabled;
        public bool SelectionPending => pendingSelection != 0;
        public string LastResult { get; private set; } = "Ready";
        public bool PanelOpen => GetComponent<GluttonyPrototypeView>()?.PanelOpen == true;
        public bool OwnerReady => isActiveAndEnabled && isOwned && NetworkClient.active && PrototypeEnabled &&
            bridge != null && bridge.EventIds != null && player != null && player.IsRuntimeInitialized && player.IsLocalOwnerBound &&
            !player.IsMenuInputBlocked && !player.IsUpgradeSelectionLocked && !player.IsRunLoadingLocked && !PanelOpen &&
            build != null && build.IsBuildActive && selection != null && selection.HasOwnerBaseline &&
            (selectionPresentation == null || !selectionPresentation.BlocksPrototypeInputThisFrame) &&
            combatant != null && combatant.IsAlive && !BootGameplayNetworkManager.CombatHasEnded;

        private void Awake()
        {
            player = GetComponent<PlayerMovement>();
            build = GetComponent<PlayerBuildRuntime>();
            selection = GetComponent<NetworkModifierSelection>();
            bridge = GetComponent<MirrorNetworkCombatBridge>();
            combatant = GetComponent<CombatantBehaviour>();
            selectionPresentation = GetComponent<ModifierSelectionController>();
            foreach (var component in GetComponents<MonoBehaviour>())
                if (component is IPrototypeAbilityModule module) RegisterModule(module);
        }

        public void RegisterModule(IPrototypeAbilityModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (modules.TryGetValue(module.AbilityId, out var existing) && existing != module)
                throw new InvalidOperationException("Duplicate prototype module: " + module.AbilityId);
            modules[module.AbilityId] = module;
        }

        public override void OnStartServer()
        {
            serverOwner = connectionToClient;
            state = new PrototypeSelectionState { Ability = PrototypeAbilityId.Gluttony, Revision = 1,
                Enabled = NetworkCombatWorld.Instance.PrototypesEnabled };
        }
        public override void OnStartAuthority() => BindInput();
        private void OnEnable() { if (isOwned && NetworkClient.active) BindInput(); }
        private void BindInput() => player?.BindPrototypeInput(RequestSelect, RequestAction);
        private void ReleaseOwner()
        {
            player?.UnbindPrototypeInput(RequestSelect, RequestAction);
            pendingSelection = 0;
            receipt = default;
        }
        public override void OnStopAuthority() => ReleaseOwner();
        public override void OnStopClient() => ReleaseOwner();
        public override void OnStopServer() => CancelEffects();
        private void OnDisable()
        {
            ReleaseOwner();
            if (isServer) CancelEffects();
        }

        private void Update()
        {
            if (!isServer || state.Revision == 0) return;
            var world = NetworkCombatWorld.Instance;
            bool alive = world != null && !world.Gateway.CombatStopped && !BootGameplayNetworkManager.CombatHasEnded &&
                world.Gateway.Ledger.IsAlive(netId);
            if ((wasAlive && !alive) || serverOwner != connectionToClient)
            {
                CancelEffects();
                var next = state;
                next.Revision = NextRevision(state.Revision);
                state = next;
            }
            wasAlive = alive;
            serverOwner = connectionToClient;
        }

        public bool OwnerCanBegin(PrototypeAbilityId ability) => OwnerReady && !SelectionPending && SelectedAbility == ability;
        public bool RequestSelect(PrototypeAbilityId ability)
        {
            if (!OwnerReady || SelectionPending || ability == SelectedAbility || !IsKnownAbility(ability)) return false;
            pendingSelection = bridge.EventIds.Next().Value;
            CmdSelect(pendingSelection, SelectionRevision, ability);
            return true;
        }

        public bool RequestAction(PrototypeAbilityAction action)
        {
            if (!OwnerReady) return false;
            // A started performance keeps its rhythm input after changing the selected module.
            foreach (var module in modules.Values)
                if (module.TryHandleOngoingAction(action)) return true;
            if (SelectionPending) return false;
            if (modules.TryGetValue(SelectedAbility, out var selected)) return selected.TryHandleAction(action);
            LastResult = DisplayName(SelectedAbility) + " - 待实现 / coming in a later stage";
            return false;
        }

        private static bool IsKnownAbility(PrototypeAbilityId ability) => Enum.IsDefined(typeof(PrototypeAbilityId), ability);
        private bool ServerReady()
        {
            var world = NetworkCombatWorld.Instance;
            return isServer && isActiveAndEnabled && state.Revision != 0 && state.Enabled && world != null &&
                !world.Gateway.CombatStopped && !BootGameplayNetworkManager.CombatHasEnded &&
                world.Gateway.Ledger.IsAlive(netId) && !world.Gateway.Ledger.IsPlayerSelectingUpgrade(netId) &&
                player != null && player.IsRuntimeInitialized && !player.IsRunLoadingLocked && !player.IsMenuInputBlocked &&
                build != null && build.IsBuildActive && selection != null && selection.isActiveAndEnabled;
        }

        public bool ServerCanBegin(PrototypeAbilityId ability, uint revision) =>
            ServerReady() && revision != 0 && revision == state.Revision && state.Ability == ability;

        [Command(channel = Channels.Reliable)]
        private void CmdSelect(ulong id, uint revision, PrototypeAbilityId ability, NetworkConnectionToClient sender = null)
        {
            var world = NetworkCombatWorld.Instance;
            if (sender == null || sender != connectionToClient || sender.identity != netIdentity || !sender.isReady ||
                !sender.isAuthenticated || world == null) return;
            uint sequence = new CombatEventId(id).Sequence;
            bool valid = sequence > lastRequestSequence && world.Gateway.ClientIdentities.Validate(netId, id, sequence);
            if (valid) lastRequestSequence = sequence;
            valid &= ServerReady() && revision == state.Revision && IsKnownAbility(ability) && state.Ability != ability;
            if (valid)
            {
                var next = state;
                next.Ability = ability;
                next.Revision = NextRevision(state.Revision);
                state = next;
            }
            TargetSelectionResult(sender, id, valid, state);
        }

        [TargetRpc]
        private void TargetSelectionResult(NetworkConnectionToClient target, ulong id, bool accepted, PrototypeSelectionState snapshot)
        {
            if (!isOwned) return;
            if (pendingSelection == id) pendingSelection = 0;
            if (GluttonyReplica.IsNewer(snapshot.Revision, receipt.Revision)) receipt = snapshot;
            LastResult = accepted ? DisplayName(snapshot.Ability) : "Switch rejected";
        }

        [Server]
        public void ServerSetEnabled(bool enabled)
        {
            if (state.Enabled == enabled) return;
            var next = state;
            next.Enabled = enabled;
            next.Revision = NextRevision(state.Revision);
            state = next;
            if (!enabled) CancelEffects();
        }
        private void CancelEffects()
        {
            foreach (var module in modules.Values) module.ServerCancelEffects();
        }
        private static uint NextRevision(uint current) => current == uint.MaxValue ? 1 : current + 1;
        public static string DisplayName(PrototypeAbilityId ability) => ability == PrototypeAbilityId.Gluttony ? "吞噬 / Gluttony" :
            ability == PrototypeAbilityId.Music ? "音乐 / Music" : ability == PrototypeAbilityId.Allure ? "魅惑 / Allure" : ability.ToString();
        public string SelectedStatus => modules.TryGetValue(SelectedAbility, out var module) ? module.StatusText : "待实现 / Not implemented yet";
    }
}
