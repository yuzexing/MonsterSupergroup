using System;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkPlayerBootstrap), typeof(MirrorNetworkCombatBridge))]
    public sealed class NetworkPlayerDash : NetworkBehaviour
    {
        private PlayerMovement movement;
        private PlayerBuildRuntime build;
        private NetworkModifierSelection selection;
        private MirrorNetworkCombatBridge bridge;
        private ServerDashUseRegistry serverUses;
        private PlayerDashPrediction prediction;
        private PlayerDashSnapshot? preparedSnapshot;
        private uint serverRevision;
        private ulong lastProcessedUseId;
        private ulong lastRejectedUseId;
        private bool ownerBound;
        private PlayerStats serverStats;

        public bool HasOwnerBaseline => prediction != null && prediction.HasBaseline;
        public PlayerDashRuntime OwnerRuntime => prediction?.Runtime;
        public int AcceptedUseCount { get; private set; }
        public int RejectedUseCount { get; private set; }
        public int PendingOwnerUseCount => prediction?.PendingCount ?? 0;
        public event Action<ulong> OwnerUseRejected;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            build = GetComponent<PlayerBuildRuntime>();
            selection = GetComponent<NetworkModifierSelection>();
            bridge = GetComponent<MirrorNetworkCombatBridge>();
        }

        public void PrepareServerRestore(PlayerDashSnapshot snapshot)
        {
            if (netIdentity != null && netId != 0)
                throw new InvalidOperationException("Dash restoration must precede avatar spawning.");
            // Copy and validate before storing a reconnect checkpoint.
            var validated = new PlayerDashRuntime();
            validated.Restore(snapshot, 0d);
            preparedSnapshot = validated.Capture(0d);
        }

        public override void OnStartServer()
        {
            GetComponent<NetworkPlayerBootstrap>().EnsurePlayerRuntimeInitialized();
            serverUses = new ServerDashUseRegistry();
            if (preparedSnapshot.HasValue) serverUses.Runtime.Restore(preparedSnapshot.Value, NetworkTime.time);
            preparedSnapshot = null;
            serverStats = movement.PlayerStats;
            serverUses.Runtime.ConfigureCapacity(serverStats.currentStats.maxDashCharges);
            serverStats.MaximumDashesChanged += HandleServerCapacityChanged;
        }

        public override void OnStartAuthority()
        {
            if (ownerBound || !isActiveAndEnabled || GameplayRuntimeEnvironment.IsDedicatedServer) return;
            GetComponent<NetworkPlayerBootstrap>().EnsurePlayerRuntimeInitialized();
            prediction = new PlayerDashPrediction();
            ownerBound = true;
            movement.BindDashRuntime(prediction.Runtime, () => NetworkTime.time, TryCommitOwnerUse);
            CmdRequestBaseline();
        }

        private void OnEnable()
        {
            if (movement != null && isOwned && NetworkClient.active) OnStartAuthority();
        }

        private ulong TryCommitOwnerUse(DashMotionParameters motion)
        {
            if (!ownerBound || !isOwned || !NetworkClient.active || !isActiveAndEnabled ||
                prediction == null || !prediction.HasBaseline || bridge.EventIds == null ||
                selection == null || !selection.HasOwnerBaseline || movement.IsUpgradeSelectionLocked ||
                build == null || !build.IsBuildActive) return 0;
            double now = NetworkTime.time;
            ulong useId = bridge.EventIds.Next().Value;
            float recharge = movement.PlayerStats.currentStats.dashCooldown;
            if (!prediction.TryPredict(useId, now, motion.Duration, recharge)) return 0;
            // This reliable command precedes OnDashStart and every resulting weapon-root command.
            // Add pending input first because a Host TargetRpc may run synchronously here.
            CmdUseDash(useId, selection.OwnerBuildRevision, now, motion);
            return ownerBound && lastRejectedUseId != useId ? useId : 0;
        }

        [Command(channel = Channels.Reliable)]
        private void CmdRequestBaseline(NetworkConnectionToClient sender = null)
        {
            if (sender == connectionToClient && sender != null && serverUses != null) SendBaseline(0);
        }

        [Command(channel = Channels.Reliable)]
        private void CmdUseDash(ulong useId, uint buildRevision, double ownerTime, DashMotionParameters motion,
            NetworkConnectionToClient sender = null)
        {
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            var id = new CombatEventId(useId);
            if (sender == null || sender != connectionToClient || world == null || serverUses == null ||
                !world.Gateway.ClientIdentities.Validate(bridge.OwnerPlayerId, useId, id.Sequence)) return;
            if (id.Sequence <= new CombatEventId(lastProcessedUseId).Sequence)
            { RejectUse(useId); return; }
            lastProcessedUseId = useId;
            double now = NetworkTime.time;
            if (!isActiveAndEnabled || selection == null || buildRevision == 0 || buildRevision != selection.BuildRevision ||
                !world.Gateway.Ledger.TryGetState(netId, out var state) || !state.Alive ||
                world.Gateway.Ledger.IsPlayerSelectingUpgrade(netId) ||
                !Finite(ownerTime) || ownerTime < now - 2d || ownerTime > now + 0.1d ||
                !ValidateMotion(motion, out DashMotionParameters canonicalMotion))
            { RejectUse(useId); return; }
            var weapons = new uint[PlayerBuildRuntime.HandSlotCount];
            for (int slot = 0; slot < weapons.Length; slot++)
                if (build.GetWeaponAtSlot(slot) is DashAttackBehaviour weapon) weapons[slot] = weapon.WeaponData.ID;
            if (!serverUses.TryConsume(useId, buildRevision, now, canonicalMotion.Duration,
                    movement.PlayerStats.currentStats.dashCooldown, weapons))
            { RejectUse(useId); return; }
            AcceptedUseCount++;
            SendBaseline(0);
        }

        private bool ValidateMotion(DashMotionParameters motion, out DashMotionParameters canonicalMotion)
        {
            canonicalMotion = default;
            if (!Finite(motion.StartPosition.x) || !Finite(motion.StartPosition.y) ||
                !Finite(motion.Direction.x) || !Finite(motion.Direction.y) ||
                !Finite(motion.Distance) || !Finite(motion.Duration) || !Finite(motion.PeakSpeed) ||
                motion.Distance < 0f || motion.Duration < 0f || motion.PeakSpeed <= 0f ||
                Math.Abs(motion.Direction.sqrMagnitude - 1f) > 0.01f) return false;
            float maximumDistance = movement.PlayerStats.currentStats.dashDistance;
            // Movement remains Owner simulated. Allow the avatar's normal transform replication lag,
            // while checking the same authored curve and obstacle geometry at the reported origin.
            if (Vector2.Distance(movement.transform.position, motion.StartPosition) > maximumDistance + 1f ||
                !movement.TryGetDashMotionParameters(motion.Direction, motion.StartPosition, out canonicalMotion)) return false;
            return Math.Abs(canonicalMotion.Distance - motion.Distance) <= 0.05f &&
                Math.Abs(canonicalMotion.Duration - motion.Duration) <= 0.01f &&
                Math.Abs(canonicalMotion.PeakSpeed - motion.PeakSpeed) <= 0.01f;
        }

        private void RejectUse(ulong useId)
        {
            RejectedUseCount++;
            SendBaseline(useId);
        }

        private void SendBaseline(ulong rejectedUseId)
        {
            if (connectionToClient == null || serverUses == null) return;
            serverRevision = unchecked(serverRevision + 1);
            if (serverRevision == 0) serverRevision = 1;
            TargetApplyBaseline(connectionToClient, serverRevision, lastProcessedUseId,
                serverUses.Runtime.Capture(NetworkTime.time), rejectedUseId);
        }

        [TargetRpc(channel = Channels.Reliable)]
        private void TargetApplyBaseline(NetworkConnectionToClient target, uint revision, ulong acknowledgedUseId,
            PlayerDashSnapshot snapshot, ulong rejectedUseId)
        {
            if (!ownerBound || prediction == null ||
                !prediction.ApplyBaseline(revision, acknowledgedUseId, snapshot, NetworkTime.time)) return;
            if (rejectedUseId != 0)
            {
                lastRejectedUseId = rejectedUseId;
                movement.CancelDash(rejectedUseId);
                for (int slot = 0; slot < PlayerBuildRuntime.HandSlotCount; slot++)
                    if (build.GetWeaponAtSlot(slot) is DashAttackBehaviour weapon) weapon.CancelDashUse(rejectedUseId);
                OwnerUseRejected?.Invoke(rejectedUseId);
            }
        }

        public bool CanAdmitWeapon(ulong useId, uint revision, int slot, uint weaponId) =>
            isServer && serverUses != null && serverUses.CanAdmitWeapon(useId, revision, slot, weaponId, NetworkTime.time);

        public void MarkWeaponAdmitted(ulong useId, int slot) => serverUses.MarkWeaponAdmitted(useId, slot);

        public PlayerDashSnapshot CaptureServerState()
        {
            if (!isServer || serverUses == null) throw new InvalidOperationException("Only the server holds a canonical dash checkpoint.");
            return serverUses.Runtime.Capture(NetworkTime.time);
        }

        private void HandleServerCapacityChanged(int capacity)
        {
            serverUses?.Runtime.ConfigureCapacity(capacity);
            // Send only after the client has requested its first post-spawn baseline.
            if (serverRevision != 0) SendBaseline(0);
        }

        private void ReleaseOwner()
        {
            if (!ownerBound) return;
            ownerBound = false;
            movement?.UnbindDashRuntime();
            prediction = null;
        }

        public override void OnStopAuthority() => ReleaseOwner();
        public override void OnStopClient() => ReleaseOwner();
        public override void OnStopServer()
        {
            if (serverStats != null) serverStats.MaximumDashesChanged -= HandleServerCapacityChanged;
            serverStats = null;
            serverUses?.ClearPermits();
        }
        private void OnDisable() { ReleaseOwner(); serverUses?.ClearPermits(); }
        private void OnDestroy() { ReleaseOwner(); OnStopServer(); }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
