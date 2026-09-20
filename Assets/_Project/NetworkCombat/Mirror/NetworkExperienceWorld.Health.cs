using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Player;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkExperienceWorld
    {
        private sealed class HealthClaim
        {
            public NetworkExperienceGem Item;
            public ulong Member;
            public ushort Epoch;
            public uint Avatar, Version;
            public float Elapsed;
            public double LastTime;
            public bool Authorized;
        }
        private readonly Dictionary<ulong, HealthClaim> claims = new();
        private readonly List<ulong> claimIteration = new();
        private readonly Dictionary<ulong, (uint avatar, uint claim, ushort epoch)> committedClaims = new();
        private PickupDefinitionSnapshot xpDefinition, healthDefinition;
        private bool endedCleared;
        public int HealthCount => CountEffect(PickupEffect.RestoreHealth);
        public int PendingHealthCount => claims.Count;
        private int CountEffect(PickupEffect effect)
        { int count = 0; foreach (var item in drops.Values) if (item != null && item.Effect == effect) count++; return count; }
        public PickupDefinitionSnapshot Definition(PickupEffect effect) => effect == PickupEffect.RestoreHealth ? healthDefinition : xpDefinition;
        private void CapturePickupDefinitions()
        {
            xpDefinition = pickupRules?.experienceItem?.Capture();
            healthDefinition = pickupRules?.healthItem?.Capture();
        }
        private bool ReserveHealth(NetworkExperienceGem item, RunParticipant member, NetworkIdentity avatar, out string reason)
        {
            if (healthDefinition == null || !combat.Gateway.Ledger.TryGetState(avatar.netId, out var health) ||
                health.Health >= health.MaxHealth) { reason = "full-or-unready"; return false; }
            item.BeginHealthFlight(avatar.netId);
            claims.Add(item.DropId, new HealthClaim { Item = item, Member = member.Id, Epoch = member.ConnectionEpoch,
                Avatar = avatar.netId, Version = item.ClaimVersion, LastTime = EnemySimulationClock.CombatNow });
            PickupAudit.Emit("reserved", runId, item.DropId, $"member={member.Id};avatar={avatar.netId};claim={item.ClaimVersion}");
            reason = "flying"; return true;
        }
        private bool ClaimOwner(HealthClaim claim, out NetworkIdentity avatar)
        {
            avatar = null;
            var manager = NetworkManager.singleton as BootGameplayNetworkManager;
            return manager != null && NetworkServer.spawned.TryGetValue(claim.Avatar, out avatar) && avatar != null &&
                avatar.connectionToClient != null && avatar.connectionToClient.isReady &&
                manager.Session.TryGetConnection(avatar.connectionToClient.connectionId, out var member) &&
                member.Id == claim.Member && member.ConnectionEpoch == claim.Epoch && member.AvatarId == claim.Avatar;
        }
        private void Update()
        {
            if (isServer && BootGameplayNetworkManager.CombatHasEnded && !endedCleared)
            { endedCleared = true; ClearServer(); return; }
            if (!isServer || claims.Count == 0) return;
            double now = EnemySimulationClock.CombatNow;
            claimIteration.Clear(); claimIteration.AddRange(claims.Keys);
            foreach (var id in claimIteration)
            {
                if (!claims.TryGetValue(id, out var claim)) continue;
                if (BootGameplayNetworkManager.CombatHasEnded || !ClaimOwner(claim, out var avatar) ||
                    !combat.Gateway.Ledger.TryGetState(claim.Avatar, out var state) || !state.Alive)
                { ReturnHealth(id, "invalid-owner-or-ended"); continue; }
                double delta = Math.Max(0, now - claim.LastTime); claim.LastTime = now;
                var selection = avatar.GetComponent<NetworkModifierSelection>();
                bool busy = selection == null || selection.IsSelecting || selection.PendingEventId != 0 ||
                    combat.Gateway.Ledger.IsPlayerSelectingUpgrade(avatar.netId);
                if (claim.Authorized) continue;
                if (state.Health >= state.MaxHealth) { ReturnHealth(id, "full-before-arrival"); continue; }
                if (!busy) claim.Elapsed = Mathf.Min(healthDefinition.FlightDuration, claim.Elapsed + (float)delta);
                claim.Item.SetFlightProgress(claim.Elapsed, busy);
                if (busy || claim.Elapsed < healthDefinition.FlightDuration) continue;
                bool anotherReceipt = false;
                foreach (var other in claims.Values)
                    if (other != claim && other.Avatar == claim.Avatar && other.Authorized) { anotherReceipt = true; break; }
                if (anotherReceipt) continue; // One outstanding health mutation per owner; other flights may finish normally.
                claim.Authorized = true;
                avatar.GetComponent<NetworkExperienceCollector>().ServerAuthorizeHealth(runId, id, claim.Version,
                    Mathf.RoundToInt(claim.Item.RawExperience));
                PickupAudit.Emit("arrived", runId, id, $"claim={claim.Version};elapsed={claim.Elapsed}");
            }
        }
        private bool ValidateHealthReceipt(uint player, PlayerHealthReport report)
        {
            if (BootGameplayNetworkManager.CombatHasEnded || report.PickupRound != round) return false;
            // Damage occurring before the commit acknowledgment retains its receipt. It must still
            // report health after the bottle is consumed, without consuming/healing a second time.
            if (committedClaims.TryGetValue(report.PickupDropId, out var committed))
                return committed.avatar == player && committed.claim == report.PickupClaimVersion &&
                    new MonsterSupergroup.GAS.CombatEventId(report.EventId).ConnectionEpoch == committed.epoch;
            return claims.TryGetValue(report.PickupDropId, out var claim) &&
                claim.Authorized && claim.Avatar == player && claim.Version == report.PickupClaimVersion &&
                // A newer owner report may include damage after the authorized heal, including death.
                // Validate the grant against the pre-report ledger; do not reject that final health state.
                ClaimOwner(claim, out _) && combat.Gateway.Ledger.IsAlive(player) && report.PickupRestoredHealth > 0 &&
                report.PickupRestoredHealth <= claim.Item.RawExperience && report.EntityId == player;
        }
        private void CommitHealthReceipt(PlayerHealthReport report)
        {
            if (report.PickupDropId == 0 || !claims.TryGetValue(report.PickupDropId, out var claim)) return;
            claims.Remove(report.PickupDropId);
            committedClaims[report.PickupDropId] = (report.EntityId, report.PickupClaimVersion,
                new MonsterSupergroup.GAS.CombatEventId(report.EventId).ConnectionEpoch);
            if (ClaimOwner(claim, out var owner)) owner.GetComponent<NetworkExperienceCollector>()
                .TargetPickupCommitted(owner.connectionToClient, runId, report.PickupDropId, claim.Version);
            drops.Remove(report.PickupDropId);
            claim.Item.ServerHealthConsumed();
            PickupAudit.Emit("health-committed", runId, report.PickupDropId,
                $"claim={report.PickupClaimVersion};restored={report.PickupRestoredHealth};health={report.Health};version={report.StateVersion}");
            RecycleEntity(claim.Item);
        }
        private void RejectedHealthReceipt(uint sender, PlayerHealthReport report, CombatRejectionReason reason)
        {
            if (report.PickupDropId == 0) return;
            combat.Gateway.Ledger.TryGetState(report.EntityId, out var state);
            PickupAudit.Emit("receipt-rejected", runId, report.PickupDropId,
                $"reason={reason};claim={report.PickupClaimVersion};round={report.PickupRound}/{round};version={report.StateVersion}/{state.StateVersion};health={report.Health}/{state.Health}");
            if (sender != report.EntityId || report.PickupRound != round || !claims.TryGetValue(report.PickupDropId, out var claim) ||
                report.PickupClaimVersion != claim.Version || claim.Avatar != report.EntityId || !ClaimOwner(claim, out var avatar)) return;
            var collector = avatar.GetComponent<NetworkExperienceCollector>();
            if (reason == CombatRejectionReason.StaleOwnerReport && state.Alive)
                collector.TargetRetryReceipt(avatar.connectionToClient, runId, report.PickupDropId, claim.Version, state.StateVersion);
            else
            {
                collector.TargetRejectReceipt(avatar.connectionToClient, runId, report.PickupDropId, claim.Version, state);
                ReturnHealth(report.PickupDropId, "receipt-rejected-" + reason);
            }
        }
        public bool RejectHealthGrant(NetworkConnectionToClient sender, string run, ulong drop, uint claimVersion)
        {
            if (run != runId || sender?.identity == null || !claims.TryGetValue(drop, out var claim) ||
                claim.Version != claimVersion || claim.Avatar != sender.identity.netId || !ClaimOwner(claim, out _)) return false;
            ReturnHealth(drop, "owner-zero-heal"); return true;
        }
        private void ReturnHealth(ulong drop, string reason)
        {
            if (!claims.Remove(drop, out var claim)) return;
            claim.Item.CancelHealthFlight();
            PickupAudit.Emit("returned", runId, drop, reason);
        }
    }
}
