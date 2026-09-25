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
        private string observedWaitReason;
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
                    gem.DropId != entry.Key || gem.RunId != pendingRun)
                {
                    if (PickupInvestigation.Enabled) PickupInvestigation.Capture("request_resolved", "Observed", pendingRun, entry.Key,
                        entry.Value.ClaimVersion, gem != null ? gem.netId : 0, netId,
                        () => new { present = gem != null && gem.isActiveAndEnabled, claimed = gem != null && gem.Claimed,
                            observedDropId = gem != null ? gem.DropId.ToString() : null,
                            observedClaimVersion = gem != null ? (uint?)gem.ClaimVersion : null,
                            observedCollector = gem != null ? (uint?)gem.CollectorId : null, benefitConfirmed = false }, "ClaimDespawnOrReuse");
                    batch.Add(entry.Key);
                }
            }
            foreach (ulong id in batch) pending.Remove(id);
            batch.Clear();
            if (player == null || progression == null ||
                !player.isActiveAndEnabled || !player.CombatantBinding.IsAlive || !progression.HasOwnerBaseline ||
                progression.IsSelecting || BootGameplayNetworkManager.CombatHasEnded)
            {
                if (PickupInvestigation.Enabled) ObserveWait(player == null || progression == null ? "MissingComponents" : !player.isActiveAndEnabled ? "PlayerInactive" :
                    !player.CombatantBinding.IsAlive ? "PlayerDead" : !progression.HasOwnerBaseline ? "AwaitingBaseline" :
                    progression.IsSelecting ? "Selecting" : "RunEnded");
                return;
            }
            if (PickupInvestigation.Enabled) ObserveWait("Ready");
            if (Time.unscaledTime < nextRequest) return;
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
                NetworkExperienceWorld.Current?.RunId != run)
            {
                if (PickupInvestigation.Enabled) PickupInvestigation.Capture("request", "Ignored", run, drop, 0, 0, netId,
                    () => new { isOwned, clientActive = NetworkClient.active, clientReady = NetworkClient.ready, isActiveAndEnabled }, "AuthorityConnectionOrRun");
                return;
            }
            if (pendingRun != run) { ClearPending(); pendingRun = run; }
            if (pending.ContainsKey(drop)) return;
            foreach (var gem in NetworkExperienceGem.ClientGems)
                if (gem != null && gem.RunId == run && gem.DropId == drop)
                { pending.Add(drop, (gem, gem.ClaimVersion)); break; }
            SubmitCollectionBatch(run, new[] { drop });
        }
        private void SubmitCollectionBatch(string run, ulong[] drops)
        {
            SubmittedCollectionBatchCount++;
            if (PickupInvestigation.Enabled)
                foreach (ulong id in drops)
                {
                    pending.TryGetValue(id, out var item);
                    PickupInvestigation.Capture("request", "Submitted", run, id, item.ClaimVersion, item.Gem != null ? item.Gem.netId : 0, netId,
                        () => new { localBatch = SubmittedCollectionBatchCount, pendingCount = pending.Count, benefitConfirmed = false });
                }
            CmdCollectBatch(run, drops);
        }
        [Server]
        public void ServerAuthorizeHealth(string run, ulong drop, uint claim, int amount) => TargetRestoreHealth(connectionToClient, run, drop, claim, amount);
        [TargetRpc]
        private void TargetRestoreHealth(NetworkConnectionToClient target, string run, ulong drop, uint claim, int amount)
        {
            if (!isOwned || BootGameplayNetworkManager.CombatHasEnded || NetworkExperienceWorld.Current?.RunId != run)
            {
                if (PickupInvestigation.Enabled) PickupInvestigation.Capture("owner_result", "Ignored", run, drop, claim, 0, netId,
                    () => new { amount, isOwned, actualRestored = 0, benefitConfirmed = false }, "AuthorityRunOrEnded");
                return;
            }
            // Reliable authorization can precede the final SyncVar sample. Arrive visually before the health event.
            foreach (var item in NetworkExperienceGem.ClientGems)
                if (item.RunId == run && item.DropId == drop && item.ClaimVersion == claim) item.PresentHealthArrival();
            int beforeHealth = PickupInvestigation.Enabled ? player.CombatantBinding.CurrentHealth : 0;
            int restored = progression == null || progression.IsSelecting || !player.CombatantBinding.IsAlive
                ? 0 : GetComponent<NetworkCombatantAdapter>().RestorePickupHealth(run, drop, claim, amount);
            if (PickupInvestigation.Enabled) ObserveOwnerResult(run, drop, claim, amount, restored, beforeHealth);
            PickupAudit.Emit("owner-result", run, drop, $"claim={claim};restored={restored};health={player.CombatantBinding.CurrentHealth}");
            if (restored == 0) CmdRejectHealth(run, drop, claim);
            else GetComponent<MirrorNetworkCombatBridge>().Flush();
        }
        private void ObserveOwnerResult(string run, ulong drop, uint claim, int amount, int restored, int beforeHealth)
        {
            if (!PickupInvestigation.Enabled) return;
            PickupInvestigation.Capture("owner_result", restored < 0 ? "ReceiptResubmitted" : restored > 0 ? "Applied" : "Rejected", run, drop, claim, 0, netId,
                () => new { authorizedAmount = amount, actualRestored = Mathf.Max(0, restored), adapterResult = restored,
                    beforeHealth, afterHealth = player.CombatantBinding.CurrentHealth, duplicate = restored < 0,
                    awaitingServerCommit = restored != 0, benefitConfirmed = false }, restored == 0 ? "ZeroRestore" : null);
        }
        [TargetRpc] internal void TargetPickupCommitted(NetworkConnectionToClient target, string run, ulong drop, uint claim)
        {
            bool matchingRun = NetworkExperienceWorld.Current?.RunId == run;
            if (PickupInvestigation.Enabled) PickupInvestigation.Capture("receipt_ack", matchingRun ? "Received" : "Ignored", run, drop, claim, 0, netId,
                () => new { matchingRun, benefitConfirmed = matchingRun }, matchingRun ? null : "WrongRun");
            if (matchingRun) GetComponent<NetworkCombatantAdapter>().AcknowledgePickup(drop, claim);
        }
        [TargetRpc] internal void TargetRetryReceipt(NetworkConnectionToClient target, string run, ulong drop, uint claim, uint version)
        {
            if (PickupInvestigation.Enabled) PickupInvestigation.Capture("retry", "Received", run, drop, claim, 0, netId,
                () => new { canonicalVersion = version, repeatedGrant = false });
            GetComponent<NetworkCombatantAdapter>().RetryPickupReceipt(run, drop, claim, version);
        }
        [TargetRpc] internal void TargetRejectReceipt(NetworkConnectionToClient target, string run, ulong drop, uint claim, CanonicalEntityState state)
        {
            if (PickupInvestigation.Enabled) PickupInvestigation.Capture("receipt_reject", "Received", run, drop, claim, 0, netId,
                () => new { canonicalHealth = state.Health, canonicalVersion = state.StateVersion, benefitConfirmed = false });
            GetComponent<NetworkCombatantAdapter>().RejectPickupReceipt(run, drop, claim, state);
        }
        [Command]
        private void CmdRejectHealth(string run, ulong drop, uint claim, NetworkConnectionToClient sender = null)
            => NetworkExperienceWorld.Current?.RejectHealthGrant(sender, run, drop, claim);
        [Command]
        private void CmdCollectBatch(string run, ulong[] drops, NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender != connectionToClient || drops == null ||
                drops.Length == 0 || drops.Length > MaximumCollectionsPerBatch)
            {
                if (PickupInvestigation.Enabled) PickupInvestigation.Capture("request", "Rejected", run, 0, 0, 0, netId,
                    () => new { connectionId = sender?.connectionId, dropCount = drops?.Length }, "InvalidBatchOrSender");
                return;
            }
            var world = NetworkExperienceWorld.Current;
            var rejected = new List<ulong>();
            var unique = new HashSet<ulong>();
            foreach (ulong drop in drops)
            {
                if (!unique.Add(drop))
                {
                    if (PickupInvestigation.Enabled) PickupInvestigation.Capture("request", "Ignored", run, drop, 0, 0, netId,
                        () => new { connectionId = sender.connectionId }, "DuplicateInBatch");
                    continue;
                }
                if (!isActiveAndEnabled || world == null || !world.TryCollect(sender, netIdentity, run, drop, out _))
                {
                    if (PickupInvestigation.Enabled && (!isActiveAndEnabled || world == null))
                        PickupInvestigation.Capture("decision", "Rejected", run, drop, 0, 0, netId,
                            () => new { isActiveAndEnabled, worldAvailable = world != null }, "InactiveOrMissingWorld");
                    rejected.Add(drop);
                }
            }
            // Reliable responses release only rejected requests. Successful requests stay
            // suppressed until their replicated claim/despawn arrives, without timeout spam.
            if (rejected.Count > 0) TargetRejectCollections(sender, run, rejected.ToArray());
        }
        [TargetRpc]
        private void TargetRejectCollections(NetworkConnectionToClient target, string run, ulong[] drops)
        {
            if (run != pendingRun || run != NetworkExperienceWorld.Current?.RunId) return;
            foreach (ulong drop in drops)
            {
                if (!PickupInvestigation.Enabled) { pending.Remove(drop); continue; }
                pending.TryGetValue(drop, out var request);
                bool removed = pending.Remove(drop);
                if (PickupInvestigation.Enabled) PickupInvestigation.Capture("request_reject", "Received", run, drop, request.ClaimVersion, 0, netId,
                    () => new { pendingRemoved = removed, benefitConfirmed = false });
            }
        }
        private void ObserveWait(string reason)
        {
            if (!PickupInvestigation.Enabled || observedWaitReason == reason) return;
            string before = observedWaitReason; observedWaitReason = reason;
            PickupInvestigation.Capture("collector_wait", "Changed", pendingRun, 0, 0, 0, netId,
                () => new { before, after = reason, pendingCount = pending.Count }, reason);
        }
        private void ClearPending()
        {
            if (PickupInvestigation.Enabled && pending.Count > 0)
                foreach (var request in pending) PickupInvestigation.Capture("request_resolved", "Cancelled", pendingRun, request.Key,
                    request.Value.ClaimVersion, 0, netId, () => new { benefitConfirmed = false }, "AuthorityOrRunLost");
            pending.Clear(); batch.Clear(); pendingRun = null; nextRequest = 0;
        }
        private void OnDisable() => ClearPending();
        public override void OnStopAuthority() => ClearPending();
        public override void OnStopClient() => ClearPending();
    }
}
