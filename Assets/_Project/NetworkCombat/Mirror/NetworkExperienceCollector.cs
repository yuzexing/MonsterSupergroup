using System.Collections.Generic;
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
        public const int MaximumCollectionsPerBatch = 32;
        private string pendingRun;
        private readonly Dictionary<ulong, (NetworkExperienceGem Gem, uint ClaimVersion)> pending = new();
        private readonly List<ulong> batch = new(MaximumCollectionsPerBatch);
        public int PendingCollectionCount => pending.Count;
        public int SubmittedCollectionBatchCount { get; private set; }
        private void Awake()
        {
            player = GetComponent<PlayerMovement>();
            progression = GetComponent<NetworkModifierSelection>();
        }
        private void Update()
        {
            if (!isOwned || !NetworkClient.active || !NetworkClient.ready || NetworkClient.connection == null)
            { ClearPending(); return; }
            var world = NetworkExperienceWorld.Current;
            if (world == null || string.IsNullOrEmpty(world.RunId)) { ClearPending(); return; }
            if (pendingRun != world.RunId) { ClearPending(); pendingRun = world.RunId; }
            // Claims/despawns are the success confirmation, including another player's claim.
            // Pooled objects may already represent a different drop by the next scan.
            batch.Clear();
            foreach (var entry in pending)
            {
                var gem = entry.Value.Gem;
                if (gem == null || !gem.isActiveAndEnabled || gem.Claimed || gem.ClaimVersion != entry.Value.ClaimVersion ||
                    gem.DropId != entry.Key || gem.RunId != pendingRun) batch.Add(entry.Key);
            }
            foreach (ulong id in batch) pending.Remove(id);
            batch.Clear();
            if (player == null || progression == null ||
                !player.isActiveAndEnabled || !player.CombatantBinding.IsAlive || !progression.HasOwnerBaseline ||
                progression.IsSelecting || BootGameplayNetworkManager.CombatHasEnded || Time.unscaledTime < nextRequest) return;
            nextRequest = Time.unscaledTime + 0.1f;
            float radius = player.PlayerStats.currentStats.pullArea;
            if (!ExperienceParameters.Finite(radius) || radius <= 0) return;
            foreach (var gem in NetworkExperienceGem.ClientGems)
                if (gem != null && !gem.Claimed && gem.RunId == world.RunId && !pending.ContainsKey(gem.DropId) &&
                    (gem.Effect != PickupEffect.RestoreHealth || player.CombatantBinding.CurrentHealth < player.CombatantBinding.MaximumHealth) &&
                    ((Vector2)transform.position - (Vector2)gem.transform.position).sqrMagnitude <= radius * radius)
                {
                    pending.Add(gem.DropId, (gem, gem.ClaimVersion));
                    batch.Add(gem.DropId);
                    if (batch.Count == MaximumCollectionsPerBatch) break;
                }
            // Build the batch before invoking a Host command, which may recycle gems synchronously.
            if (batch.Count > 0) SubmitCollectionBatch(world.RunId, batch.ToArray());
        }
        public void RequestCollection(string run, ulong drop)
        {
            if (!isOwned || !NetworkClient.active || !NetworkClient.ready || NetworkClient.connection == null || !isActiveAndEnabled ||
                NetworkExperienceWorld.Current?.RunId != run) return;
            if (pendingRun != run) { ClearPending(); pendingRun = run; }
            if (pending.ContainsKey(drop)) return;
            foreach (var gem in NetworkExperienceGem.ClientGems)
                if (gem != null && gem.RunId == run && gem.DropId == drop)
                { pending.Add(drop, (gem, gem.ClaimVersion)); break; }
            SubmitCollectionBatch(run, new[] { drop });
        }
        private void SubmitCollectionBatch(string run, ulong[] drops)
        { SubmittedCollectionBatchCount++; CmdCollectBatch(run, drops); }
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
        private void CmdCollectBatch(string run, ulong[] drops, NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender != connectionToClient || drops == null ||
                drops.Length == 0 || drops.Length > MaximumCollectionsPerBatch) return;
            var world = NetworkExperienceWorld.Current;
            var rejected = new List<ulong>();
            var unique = new HashSet<ulong>();
            foreach (ulong drop in drops)
            {
                if (!unique.Add(drop)) continue;
                if (!isActiveAndEnabled || world == null || !world.TryCollect(sender, netIdentity, run, drop, out _))
                    rejected.Add(drop);
            }
            // Reliable responses release only rejected requests. Successful requests stay
            // suppressed until their replicated claim/despawn arrives, without timeout spam.
            if (rejected.Count > 0) TargetRejectCollections(sender, run, rejected.ToArray());
        }
        [TargetRpc]
        private void TargetRejectCollections(NetworkConnectionToClient target, string run, ulong[] drops)
        {
            if (run != pendingRun || run != NetworkExperienceWorld.Current?.RunId) return;
            foreach (ulong drop in drops) pending.Remove(drop);
        }
        private void ClearPending() { pending.Clear(); batch.Clear(); pendingRun = null; nextRequest = 0; }
        private void OnDisable() => ClearPending();
        public override void OnStopAuthority() => ClearPending();
        public override void OnStopClient() => ClearPending();
    }
}
