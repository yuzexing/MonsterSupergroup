using System;
using System.Linq;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    [Serializable] public sealed class StatusRegistryReplayState { public CanonicalStatusState[] instances; public StatusRemovalReplayState[] removals; }
    [Serializable] public struct StatusRemovalReplayState { public ulong id; public uint target, version; public double expiresAt; }
    public sealed partial class ServerStatusRegistry
    {
        public StatusRegistryReplayState CaptureReplayState() => new StatusRegistryReplayState {
            instances = instances.Values.OrderBy(i => i.InstanceId.Value).Select(CanonicalStatusState.From).ToArray(),
            removals = removalVersions.OrderBy(p => p.Key.Value).Select(p => new StatusRemovalReplayState {
                id = p.Key.Value, target = p.Value.TargetEntityId, version = p.Value.Version, expiresAt = p.Value.ExpiresAt }).ToArray() };
        public static ServerStatusRegistry RestoreReplayState(CombatLedger ledger, StatusRegistryReplayState state)
        {
            var result = new ServerStatusRegistry(ledger);
            foreach (var s in state.instances) result.instances.Add(new StatusInstanceId(s.InstanceId), s.ToStatusInstance());
            foreach (var s in state.removals) result.removalVersions.Add(new StatusInstanceId(s.id), new RemovalRecord(s.target, s.version, s.expiresAt));
            return result;
        }
    }
    [Serializable] public struct AttackRootReplayState { public ulong id; public uint source, weapon, revision; public EnemyKnockbackSettings knockback; }
    [Serializable] public struct AttackPlayerReplayState { public uint player, lastSequence; public AttackRootReplayState[] roots; }
    [Serializable] public sealed class AttackRegistryReplayState { public int capacity; public AttackPlayerReplayState[] players; }
    public sealed partial class ServerAttackRegistry
    {
        public AttackRegistryReplayState CaptureReplayState() => new AttackRegistryReplayState { capacity = maximumActiveRootsPerPlayer,
            players = players.OrderBy(p => p.Key).Select(p => new AttackPlayerReplayState { player = p.Key, lastSequence = p.Value.LastRootSequence,
                roots = p.Value.Roots.OrderBy(r => r.Key).Select(r => new AttackRootReplayState { id = r.Key, source = r.Value.SourceEntityId,
                    weapon = r.Value.WeaponId, revision = r.Value.BuildRevision, knockback = Diagnostics.DiagnosticPayload.Freeze(r.Value.Knockback) }).ToArray() }).ToArray() };
        public static ServerAttackRegistry RestoreReplayState(AttackRegistryReplayState state)
        {
            var result = new ServerAttackRegistry(state.capacity);
            foreach (var p in state.players)
            {
                var player = new PlayerAttacks { LastRootSequence = p.lastSequence }; result.players.Add(p.player, player);
                foreach (var r in p.roots) player.Roots.Add(r.id, new Root(r.source, r.weapon, r.revision, r.knockback));
            }
            return result;
        }
    }
    [Serializable] public struct StatusAdmissionReplayState { public CanonicalStatusState state; public ulong predictedParent; public double expiresAt; public int budget, acceptedTicks; }
    public sealed partial class ServerStatusDamageAdmissions
    {
        public StatusAdmissionReplayState[] CaptureReplayState()
        {
            var budgets = new System.Collections.Generic.List<TickBudget>();
            return receipts.Select(r => { int id = budgets.IndexOf(r.Budget); if (id < 0) { id = budgets.Count; budgets.Add(r.Budget); }
                return new StatusAdmissionReplayState { state = r.State, predictedParent = r.PredictedParent, expiresAt = r.ExpiresAt,
                    budget = id, acceptedTicks = r.Budget.AcceptedTicks }; }).ToArray();
        }
        public static ServerStatusDamageAdmissions RestoreReplayState(StatusAdmissionReplayState[] state)
        {
            var result = new ServerStatusDamageAdmissions(); var budgets = new System.Collections.Generic.Dictionary<int, TickBudget>();
            foreach (var s in state)
            {
                if (!budgets.TryGetValue(s.budget, out var budget)) budgets.Add(s.budget, budget = new TickBudget { AcceptedTicks = s.acceptedTicks });
                result.receipts.Add(new Receipt(s.state, s.predictedParent, s.expiresAt, budget));
            }
            return result;
        }
    }
}
