using AstralShift.HellMaiden.Player;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    public sealed class NetworkExperienceCollector : NetworkBehaviour
    {
        private PlayerMovement player;
        private NetworkModifierSelection progression;
        private float nextRequest;
        private void Awake()
        {
            player = GetComponent<PlayerMovement>();
            progression = GetComponent<NetworkModifierSelection>();
        }
        private void Update()
        {
            if (!isOwned || !NetworkClient.active || player == null || progression == null ||
                !player.isActiveAndEnabled || !player.CombatantBinding.IsAlive || !progression.HasOwnerBaseline ||
                progression.IsSelecting || Time.unscaledTime < nextRequest) return;
            var world = NetworkExperienceWorld.Current;
            if (world == null || string.IsNullOrEmpty(world.RunId)) return;
            nextRequest = Time.unscaledTime + 0.1f;
            float radius = player.PlayerStats.currentStats.pullArea;
            // Send at most one intent per pass; rejection is safe to retry. No predicted claim or root motion.
            foreach (var gem in NetworkExperienceGem.ClientGems)
                if (gem != null && !gem.Claimed && gem.RunId == world.RunId &&
                    (gem.Effect != PickupEffect.RestoreHealth || player.CombatantBinding.CurrentHealth < player.CombatantBinding.MaximumHealth) &&
                    ((Vector2)transform.position - (Vector2)gem.transform.position).sqrMagnitude <= radius * radius)
                { RequestCollection(gem.RunId, gem.DropId); break; }
        }
        public void RequestCollection(string run, ulong drop)
        {
            if (isOwned && NetworkClient.active && isActiveAndEnabled) CmdCollect(run, drop);
        }
        [Server]
        public void ServerAuthorizeHealth(string run, ulong drop, uint claim, int amount) => TargetRestoreHealth(connectionToClient, run, drop, claim, amount);
        [TargetRpc]
        private void TargetRestoreHealth(NetworkConnectionToClient target, string run, ulong drop, uint claim, int amount)
        {
            if (!isOwned || BootGameplayNetworkManager.CombatHasEnded || NetworkExperienceWorld.Current?.RunId != run) return;
            // Reliable authorization can precede the final SyncVar sample. Arrive visually before the health event.
            foreach (var item in NetworkExperienceGem.ClientGems)
                if (item.RunId == run && item.DropId == drop && item.ClaimVersion == claim) item.PresentHealthArrival();
            int restored = progression == null || progression.IsSelecting || !player.CombatantBinding.IsAlive
                ? 0 : GetComponent<NetworkCombatantAdapter>().RestorePickupHealth(run, drop, claim, amount);
            PickupAudit.Emit("owner-result", run, drop, $"claim={claim};restored={restored};health={player.CombatantBinding.CurrentHealth}");
            if (restored == 0) CmdRejectHealth(run, drop, claim);
            else GetComponent<MirrorNetworkCombatBridge>().Flush();
        }
        [TargetRpc] internal void TargetPickupCommitted(NetworkConnectionToClient target, string run, ulong drop, uint claim)
        { if (NetworkExperienceWorld.Current?.RunId == run) GetComponent<NetworkCombatantAdapter>().AcknowledgePickup(drop, claim); }
        [TargetRpc] internal void TargetRetryReceipt(NetworkConnectionToClient target, string run, ulong drop, uint claim, uint version)
            => GetComponent<NetworkCombatantAdapter>().RetryPickupReceipt(run, drop, claim, version);
        [TargetRpc] internal void TargetRejectReceipt(NetworkConnectionToClient target, string run, ulong drop, uint claim, CanonicalEntityState state)
            => GetComponent<NetworkCombatantAdapter>().RejectPickupReceipt(run, drop, claim, state);
        [Command]
        private void CmdRejectHealth(string run, ulong drop, uint claim, NetworkConnectionToClient sender = null)
            => NetworkExperienceWorld.Current?.RejectHealthGrant(sender, run, drop, claim);
        [Command]
        private void CmdCollect(string run, ulong drop, NetworkConnectionToClient sender = null)
        {
            if (!isActiveAndEnabled) return;
            var world = NetworkExperienceWorld.Current;
            if (world != null && !world.TryCollect(sender, netIdentity, run, drop, out string reason))
                if (AstralShift.DebugTools.DBL.VerboseEnabled) Debug.Log($"[XP] run={run} drop={drop} requester={netId} rejected={reason}");
        }
        public override void OnStopAuthority() { nextRequest = 0; }
        public override void OnStopClient() { nextRequest = 0; }
    }
}
