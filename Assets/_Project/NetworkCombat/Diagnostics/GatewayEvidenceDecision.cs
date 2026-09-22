using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    internal sealed class GatewayEvidenceDecision : IDisposable
    {
        [ThreadStatic] private static GatewayEvidenceDecision current;
        private readonly GatewayEvidenceDecision previous;
        private readonly ServerCombatGateway gateway;
        private readonly uint sender, batch;
        private object input;
        private ulong eventId, root, parent;
        private uint target;
        private CanonicalEntityState before;
        private readonly List<(ulong id, uint target, uint version)> accepted = new();
        private GatewayEvidenceDecision(ServerCombatGateway gateway, uint sender, CombatSubmissionBatch input)
        {
            previous = current; current = this; this.gateway = gateway; this.sender = sender; batch = input.BatchSequence; this.input = input;
        }
        public static GatewayEvidenceDecision Begin(ServerCombatGateway gateway, uint sender, CombatSubmissionBatch batch) =>
            CombatEvidence.Enabled ? new GatewayEvidenceDecision(gateway, sender, batch) : null;
        private void Select(ulong id, uint entity, object value, ulong rootId = 0, ulong parentId = 0)
        { eventId = id; target = entity; input = value; root = rootId; parent = parentId; gateway.Ledger.TryGetState(target, out before); }
        public static void Select(CombatResult r) => current?.Select(r.EventId, r.TargetEntityId, r, r.RootEventId, r.ParentEventId);
        public static void Select(StatusMutation r) => current?.Select(r.EventId, r.TargetEntityId, r, r.RootEventId, r.ParentEventId);
        public static void Select(EnemyDeathReport r) => current?.Select(r.EventId, r.TargetEntityId, r, parentId: r.CauseEventId);
        public static void Select(PlayerHealthReport r) => current?.Select(r.EventId, r.EntityId, r);
        public static void Reject(string reason) => current?.Record("Rejected", reason, current.before);
        public static void Accept(object after)
        {
            if (current == null) return;
            current.Record("Accepted", "None", after);
            if (after is CanonicalEntityState state) current.accepted.Add((current.eventId, state.EntityId, state.StateVersion));
        }
        private void Record(string outcome, string reason, object after) => CombatEvidence.Event("Server", "gateway.decision", outcome, reason,
            eventId, sender, target, input, before, after, root, parent, batch);
        public void Link(CanonicalWorldBatch canonical)
        {
            foreach (var edge in accepted)
                foreach (var state in canonical.Entities ?? Array.Empty<CanonicalEntityState>())
                    if (edge.target == state.EntityId)
                    {
                        CombatEvidence.Event("Server", "gateway.canonical_link", "Produced",
                            edge.version == state.StateVersion ? "Direct" : "Coalesced",
                            edge.id, sender, edge.target, before: new { appliedVersion = edge.version }, after: state,
                            batch: batch, server: canonical.ServerSequence);
                        break;
                    }
        }
        public void Dispose() => current = previous;
    }
}
