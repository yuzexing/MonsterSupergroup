using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>
    /// Bounded outcome permissions copied from accepted SourceClient statuses. These records
    /// never execute a status; they allow its remaining ticks after its weapon snapshot ends.
    /// </summary>
    public sealed partial class ServerStatusDamageAdmissions
    {
        private const double DeliveryGrace = 2d;
        private readonly List<Receipt> receipts = new List<Receipt>();

        private void EvidenceCore_Observe(StatusMutation mutation, CanonicalStatusState state, double serverTime)
        {
            Prune(serverTime);
            if (state.Removed)
            {
                foreach (Receipt prior in receipts)
                    if (prior.State.InstanceId == state.InstanceId)
                        prior.ExpiresAt = Math.Min(prior.ExpiresAt, serverTime + DeliveryGrace);
                return;
            }
            // A lower-priority application can return the unchanged existing status.
            if (state.SourceEventId != mutation.EventId || state.TickDamage <= 0 ||
                state.ExecutionAuthority != (byte)StatusExecutionAuthority.SourceClient) return;
            TickBudget budget = null;
            foreach (Receipt prior in receipts)
                if (prior.State.InstanceId == state.InstanceId)
                {
                    if (prior.State.Version == state.Version && prior.State.ApplicationRevision == state.ApplicationRevision) return;
                    if (prior.State.ApplicationRevision == state.ApplicationRevision) budget = prior.Budget;
                    prior.ExpiresAt = Math.Min(prior.ExpiresAt, serverTime + DeliveryGrace);
                }
            budget ??= new TickBudget();
            budget.AcceptedTicks = Math.Max(budget.AcceptedTicks, state.CompletedTicks);
            receipts.Add(new Receipt(state, mutation.ParentEventId,
                state.StartTime + state.TotalTicks * (double)state.TickInterval + DeliveryGrace, budget));
        }

        public CombatRejectionReason Validate(CombatResult result, double serverTime)
        {
            Prune(serverTime);
            Receipt receipt = Find(result);
            if (receipt == null) return CombatRejectionReason.InvalidStatus;
            CanonicalStatusState state = receipt.State;
            // StartTime is the registry's server baseline, not an owner-provided clock.
            int available = (int)Math.Min(state.TotalTicks,
                Math.Max(0d, Math.Floor((serverTime - state.StartTime + DeliveryGrace) / state.TickInterval)));
            if (result.Damage != state.TickDamage || receipt.Budget.AcceptedTicks >= available)
                return CombatRejectionReason.InvalidStatus;
            return CombatRejectionReason.None;
        }

        private void EvidenceCore_Commit(CombatResult result)
        {
            Receipt receipt = Find(result);
            if (receipt != null) receipt.Budget.AcceptedTicks = Math.Min(receipt.State.TotalTicks, receipt.Budget.AcceptedTicks + 1);
        }

        public int GetAcceptedTicks(StatusInstance instance)
        {
            foreach (var receipt in receipts)
                if (receipt.State.InstanceId == instance.InstanceId.Value &&
                    receipt.State.ApplicationRevision == instance.ApplicationRevision)
                    return receipt.Budget.AcceptedTicks;
            return 0;
        }

        private void EvidenceCore_RemovePlayer(uint playerId) => receipts.RemoveAll(item => item.State.SourcePlayerId == playerId);
        private void EvidenceCore_Prune(double serverTime) => receipts.RemoveAll(item => serverTime > item.ExpiresAt);

        public static bool IsPeriodic(CombatResult result) =>
            ((CombatTags)result.DamageTags & (CombatTags.Status | CombatTags.Periodic)) ==
            (CombatTags.Status | CombatTags.Periodic);

        private Receipt Find(CombatResult result)
        {
            if (!IsPeriodic(result) || result.StatusInstanceId == 0 || result.StatusApplicationRevision == 0) return null;
            foreach (Receipt receipt in receipts)
            {
                CanonicalStatusState state = receipt.State;
                if (result.StatusInstanceId != state.InstanceId || result.StatusApplicationRevision != state.ApplicationRevision) continue;
                if (result.SourcePlayerId != state.SourcePlayerId || result.SourceEntityId != state.SourceEntityId ||
                    result.TargetEntityId != state.TargetEntityId || result.RootEventId != state.RootEventId ||
                    result.AbilityId != state.AbilityId || result.BuildId != state.BuildId) continue;
                // Predicted ticks refer to the original hit; canonical ticks refer to the accepted
                // mutation event. Both identities authorize the same shared tick budget.
                bool predicted = result.ParentEventId == receipt.PredictedParent && receipt.PredictedParent != 0;
                bool canonical = result.ParentEventId == state.SourceEventId;
                if (!predicted && !canonical) continue;
                ushort depth = predicted ? state.SourceChainDepth : (ushort)(state.SourceChainDepth + 1);
                if (result.ChainDepth != depth) continue;
                var id = new CombatEventId(result.EventId);
                var parent = new CombatEventId(result.ParentEventId);
                if (id.SourceSlot != parent.SourceSlot || id.ConnectionEpoch != parent.ConnectionEpoch ||
                    id.Sequence <= parent.Sequence) continue;
                return receipt;
            }
            return null;
        }

        private sealed class Receipt
        {
            public Receipt(CanonicalStatusState state, ulong predictedParent, double expiresAt, TickBudget budget)
            { State = state; PredictedParent = predictedParent; ExpiresAt = expiresAt; Budget = budget; }
            public readonly CanonicalStatusState State;
            public readonly ulong PredictedParent;
            public double ExpiresAt;
            public readonly TickBudget Budget;
        }
        private sealed class TickBudget { public int AcceptedTicks; }
    }
}
