using System;
using System.Collections.Generic;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkPlayerPrototypeAbilities), typeof(AllurePrototypeView))]
    public sealed class NetworkPlayerAllure : NetworkBehaviour, IPrototypeAbilityModule
    {
        [SerializeField] private AllurePrototypeConfig config;
        [SyncVar] private AllureParameters parameters;
        [SyncVar] private AllureSnapshot wireState;
        private AllureSnapshot receipt;
        private readonly AllurePrototypeRuntime runtime = new AllurePrototypeRuntime();
        private readonly ulong[] pending = new ulong[3];
        private NetworkPlayerPrototypeAbilities abilities;
        private MirrorNetworkCombatBridge bridge;
        private AllurePrototypeView view;
        private NetworkConnectionToClient serverOwner;
        private uint lastRequestSequence;
        public PrototypeAbilityId AbilityId => PrototypeAbilityId.Allure;
        public AllureParameters Parameters => parameters;
        public AllureSnapshot State => isOwned && GluttonyReplica.IsNewer(receipt.Revision, wireState.Revision) ? receipt : wireState;
        public double DecoyRemaining => State.DecoyCastId == 0 ? 0 : Math.Max(0, State.DecoyExpiresAt - NetworkTime.time);
        public double CooldownRemaining(AllureAction action) => Math.Max(0, State.ReadyAt(action) - NetworkTime.time);
        public ulong LastRequestId { get; private set; }
        public ulong LastResolvedRequestId { get; private set; }
        public bool LastRequestAccepted { get; private set; }
        public string LastResult { get; private set; } = "R Throw / T Take / F Decoy";
        public string LastNotification { get; private set; } = "";
        public float NotificationUntil { get; private set; }
        public string StatusText => $"R Throw: {CooldownRemaining(AllureAction.Throw):0.0}s | T Take: {CooldownRemaining(AllureAction.Take):0.0}s\n" +
            $"F Decoy: {CooldownRemaining(AllureAction.Decoy):0.0}s | Affected: {State.LastAffectedCount}\n" +
            (DecoyRemaining > 0 ? $"Decoy expires in {DecoyRemaining:0.0}s\n" : "") + LastResult;
        private void Awake()
        {
            abilities = GetComponent<NetworkPlayerPrototypeAbilities>();
            bridge = GetComponent<MirrorNetworkCombatBridge>(); view = GetComponent<AllurePrototypeView>();
        }
        public override void OnStartServer()
        {
            serverOwner = connectionToClient;
            parameters = NetworkCombatWorld.Instance.GetAllureParameters(config);
        }
        public bool TryHandleAction(PrototypeAbilityAction action)
        {
            switch (action)
            {
                case PrototypeAbilityAction.Primary: return RequestAction(AllureAction.Throw);
                case PrototypeAbilityAction.Secondary: return RequestAction(AllureAction.Take);
                case PrototypeAbilityAction.Decoy: return RequestAction(AllureAction.Decoy);
                default: return false;
            }
        }
        public bool TryHandleOngoingAction(PrototypeAbilityAction action) => false;
        public bool RequestAction(AllureAction action)
        {
            if (!AllurePrototypeRuntime.IsKnown(action) || !isActiveAndEnabled || abilities == null ||
                !abilities.OwnerCanBegin(AbilityId) || !parameters.IsValid || pending[(int)action] != 0) return false;
            if (CooldownRemaining(action) > 0)
            { LastResult = $"{ActionName(action)} cooling down ({CooldownRemaining(action):0.0}s)"; return false; }
            ulong id = bridge.EventIds.Next().Value;
            pending[(int)action] = id; LastRequestId = id;
            CmdCast(id, abilities.SelectionRevision, action);
            return true;
        }
        private bool ValidSender(NetworkConnectionToClient sender) => sender != null && sender == connectionToClient &&
            sender.identity == netIdentity && sender.isReady && sender.isAuthenticated;
        [Command(channel = Channels.Reliable)]
        private void CmdCast(ulong id, uint revision, AllureAction action, NetworkConnectionToClient sender = null)
        {
            if (!ValidSender(sender)) return;
            var world = NetworkCombatWorld.Instance;
            uint sequence = new CombatEventId(id).Sequence;
            bool valid = world != null && sequence > lastRequestSequence && world.Gateway.ClientIdentities.Validate(netId, id, sequence);
            if (valid) lastRequestSequence = sequence;
            string reason = "Allure request is stale or unavailable";
            valid &= isActiveAndEnabled && abilities.ServerCanBegin(AbilityId, revision) && AllurePrototypeRuntime.IsKnown(action);
            if (valid && !runtime.CanBegin(action, parameters, NetworkTime.time))
            { valid = false; reason = ActionName(action) + " is cooling down"; }
            AllureCastResult result = default;
            if (valid)
            {
                result = world.ServerCastAllure(netId, action, id, parameters);
                valid = result.Accepted; reason = result.Reason;
            }
            if (valid)
            {
                ulong previousDecoy = runtime.State.DecoyCastId;
                if (!runtime.Accept(id, action, parameters, NetworkTime.time, result.AffectedCount,
                    result.DecoyPosition, result.DecoyExpiresAt))
                    throw new InvalidOperationException("Accepted Allure cast failed runtime admission.");
                if (action == AllureAction.Decoy && previousDecoy != 0) world.ServerCancelAllure(netId, previousDecoy);
                reason = $"{ActionName(action)}: {result.AffectedCount} enemies";
                RpcCastEffect(action, result.Positions ?? Array.Empty<Vector2>());
                if (result.OtherPlayerNetId != 0 && result.OtherPlayerNetId != netId &&
                    NetworkServer.spawned.TryGetValue(result.OtherPlayerNetId, out var other) && other != null &&
                    other.TryGetComponent<NetworkPlayerAllure>(out var otherSkill))
                    otherSkill.ServerNotify(action == AllureAction.Throw ? $"Teammate sent {result.AffectedCount} enemies to you" :
                        $"Teammate took {result.AffectedCount} enemies from you");
                if (action == AllureAction.Decoy && NetworkEnemySimulationWorld.Instance != null)
                {
                    var teammates = new List<NetworkEnemySimulationEndpoint>();
                    NetworkEnemySimulationWorld.Instance.GetEligiblePlayers(teammates);
                    foreach (var teammate in teammates)
                        if (teammate.netId != netId && teammate.TryGetComponent<NetworkPlayerAllure>(out var observer))
                            observer.ServerNotify($"Teammate placed a decoy for {result.AffectedCount} enemies");
                }
                Debug.Log($"[Allure] player={netId} cast={id} action={action} affected={result.AffectedCount}");
            }
            Publish();
            TargetCastResult(sender, id, valid, reason ?? "No eligible enemies", wireState);
        }
        [TargetRpc]
        private void TargetCastResult(NetworkConnectionToClient target, ulong id, bool accepted, string reason, AllureSnapshot snapshot)
        {
            if (!isOwned) return;
            for (int i = 0; i < pending.Length; i++) if (pending[i] == id) pending[i] = 0;
            if (GluttonyReplica.IsNewer(snapshot.Revision, receipt.Revision)) receipt = snapshot;
            LastResolvedRequestId = id; LastRequestAccepted = accepted; LastResult = reason;
            ShowNotice(reason);
        }
        [Server]
        public void ServerNotify(string message)
        { if (connectionToClient != null) TargetNotice(connectionToClient, message); }
        [TargetRpc]
        private void TargetNotice(NetworkConnectionToClient target, string message)
        { if (isOwned) ShowNotice(message); }
        private void ShowNotice(string message)
        { LastNotification = message; NotificationUntil = Time.unscaledTime + 4; }
        [ClientRpc]
        private void RpcCastEffect(AllureAction action, Vector2[] positions) => view?.ShowCast(action, positions);
        private void Publish() => wireState = runtime.State;
        private void Update()
        {
            if (!isServer || runtime.State.DecoyCastId == 0) return;
            var world = NetworkCombatWorld.Instance;
            if (world == null || !abilities.PrototypeEnabled || world.Gateway.CombatStopped ||
                BootGameplayNetworkManager.CombatHasEnded || !world.Gateway.Ledger.IsAlive(netId) ||
                connectionToClient != serverOwner || NetworkTime.time >= runtime.State.DecoyExpiresAt)
                ServerCancelEffects();
        }
        [Server]
        public void ServerCancelEffects()
        {
            ulong cast = runtime.State.DecoyCastId;
            if (cast != 0)
            {
                NetworkCombatWorld.Instance?.ServerCancelAllure(netId, cast);
                runtime.ClearDecoy(cast); Publish();
            }
        }
        [Server]
        public void ApplyServerParameters(AllureParameters settings, bool resetCooldowns = false)
        {
            if (!settings.IsValid) throw new ArgumentException("Invalid Allure parameters.");
            ServerCancelEffects(); parameters = settings;
            if (resetCooldowns) runtime.ResetCooldowns();
            Publish();
        }
        public static string ActionName(AllureAction action) => action == AllureAction.Throw ? "Throw" :
            action == AllureAction.Take ? "Take" : "Decoy";
        private void ReleaseOwner()
        {
            Array.Clear(pending, 0, pending.Length); receipt = default;
            LastRequestId = LastResolvedRequestId = 0; LastRequestAccepted = false;
            LastNotification = ""; NotificationUntil = 0; view?.Clear();
        }
        public override void OnStopAuthority() => ReleaseOwner();
        public override void OnStopClient() => ReleaseOwner();
        public override void OnStopServer() => ServerCancelEffects();
        private void OnDisable() { if (isServer) ServerCancelEffects(); ReleaseOwner(); }
    }
}
