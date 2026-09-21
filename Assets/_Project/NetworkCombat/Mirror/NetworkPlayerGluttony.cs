using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;
namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent, RequireComponent(typeof(GluttonyPrototypeView))]
    public sealed partial class NetworkPlayerGluttony : NetworkBehaviour
    {
        [SerializeField] private GluttonyPrototypeConfig config;
        [SyncVar] private GluttonyParameters parameters;
        [SyncVar] private GluttonyReplica wireState;
        private GluttonyReplica ownerReceipt;
        private GluttonySnapshot state => wireState.State;
        private GluttonyReplica EffectiveState => isOwned ? GluttonyReplica.Latest(wireState, ownerReceipt) : wireState;
        [SyncVar] private uint configRevision;
        [SyncVar] private bool baseline;
        private readonly GluttonyPrototypeRuntime runtime = new GluttonyPrototypeRuntime();
        private readonly List<uint> scratch = new List<uint>(32);
        private PlayerMovement player;
        private PlayerBuildRuntime build;
        private NetworkModifierSelection selection;
        private MirrorNetworkCombatBridge bridge;
        private CombatantBehaviour combatant;
        private GluttonyPrototypeView view;
        private ulong lastSummary;
        private NetworkConnectionToClient serverOwner;
        public GluttonyParameters Parameters => parameters;
        public GluttonySnapshot State => EffectiveState.State;
        public IEnumerable<uint> MarkedTargets => EffectiveState.Targets ?? Array.Empty<uint>();
        public int RemainingMarks => EffectiveState.Targets?.Length ?? 0;
        public string LastResult { get; private set; } = "Ready";
        public bool PanelOpen => view != null && view.PanelOpen;
        public bool HasMark(uint id) => EffectiveState.HasMark(id, NetworkTime.time);
        private void Awake()
        {
            player = GetComponent<PlayerMovement>(); build = GetComponent<PlayerBuildRuntime>();
            selection = GetComponent<NetworkModifierSelection>(); bridge = GetComponent<MirrorNetworkCombatBridge>();
            combatant = GetComponent<CombatantBehaviour>(); view = GetComponent<GluttonyPrototypeView>();
        }
        public override void OnStartServer()
        {
            serverOwner = connectionToClient;
            ApplyServerParameters(NetworkCombatWorld.Instance.GetGluttonyParameters(config), false);
            baseline = true;
        }
        public override void OnStartAuthority() => player.BindGluttonyInput(RequestMarkAtPointer);
        private void OnEnable()
        {
            if (isOwned && NetworkClient.active && player != null) player.BindGluttonyInput(RequestMarkAtPointer);
        }
        public override void OnStopAuthority() => ReleaseOwner();
        public override void OnStopClient() { ReleaseOwner(); view?.ClearVisuals(); }
        public override void OnStopServer() { EndBatch("disconnect"); baseline = false; }
        private void ReleaseOwner()
        {
            player?.UnbindGluttonyInput(RequestMarkAtPointer);
            pendingTargets.Clear(); pendingCast = pendingPassive = 0; ownerReceipt = default;
        }
        private void OnDisable()
        {
            if (isServer) EndBatch("disabled");
            ReleaseOwner(); view?.ClearVisuals();
        }
        [Server]
        public void ApplyServerParameters(GluttonyParameters value, bool resetCooldowns)
        {
            if (!value.IsValid) throw new ArgumentException("Invalid prototype parameters.");
            EndBatch("configuration"); parameters = value;
            configRevision++; if (configRevision == 0) configRevision = 1;
            if (resetCooldowns) runtime.ResetCooldowns();
            Publish();
        }
        private void Publish()
        {
            var snapshot = runtime.State; snapshot.Revision = state.Revision + 1;
            if (snapshot.Revision == 0) snapshot.Revision = 1;
            var targets = new uint[runtime.Marks.Count];
            int i = 0; foreach (uint id in runtime.Marks) targets[i++] = id;
            wireState = new GluttonyReplica { State = snapshot, Targets = targets };
        }
        private void EndBatch(string reason)
        {
            runtime.CancelMarks(); LogSummary(reason);
            if (isServer) Publish();
        }
        private void LogSummary(string reason)
        {
            var s = runtime.State;
            if (s.CastId == 0 || s.CastId == lastSummary || runtime.Marks.Count != 0) return;
            lastSummary = s.CastId;
            Debug.Log($"[Gluttony] player={netId} cast={s.CastId} marked={s.Marked} collected={s.Collected} lost={s.Lost} expired={s.Expired} cancelled={s.Cancelled} end={reason}");
        }
        private void Update()
        {
            if (isServer && baseline)
            {
                var world = NetworkCombatWorld.Instance;
                if (world == null || world.Gateway.CombatStopped || !world.Gateway.Ledger.IsAlive(netId) || serverOwner != connectionToClient)
                {
                    if (runtime.Marks.Count > 0) EndBatch("death-or-owner-change");
                    serverOwner = connectionToClient;
                }
                else
                {
                    bool changed = runtime.Expire(NetworkTime.time);
                    scratch.Clear(); foreach (uint id in runtime.Marks) scratch.Add(id);
                    foreach (uint id in scratch)
                        if (!world.Gateway.Ledger.IsAlive(id)) changed |= runtime.ForgetKilledTarget(id);
                    if (changed) { Publish(); LogSummary("complete"); }
                }
            }
            UpdateOwner();
        }
    }
}
