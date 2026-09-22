using System;
using System.Linq;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    [Serializable] public sealed class GatewayReplayState
    {
        public int version = 1;
        public bool supported, stopped;
        public uint round, serverSequence;
        public int maximumResults, maximumMutations, maximumReports;
        public LedgerReplayState ledger;
        public StatusRegistryReplayState statuses;
        public AttackRegistryReplayState attacks;
        public StatusAdmissionReplayState[] admissions;
        public ProcessedReplayState processed;
        public ClientIdentityReplayState[] identities;
        public BatchTrackerReplayState batches;
        public EventSequenceState eventIds;
        public ConfirmedKill[] deaths;
        public uint[] retired, disconnected;
    }
    public sealed partial class ServerCombatGateway
    {
        public GatewayReplayState CaptureReplayState() => new GatewayReplayState {
            supported = serverEventIds is SequentialCombatEventIdSource, stopped = CombatStopped, round = Round,
            serverSequence = serverSequence, maximumResults = MaximumResultsPerBatch, maximumMutations = MaximumStatusMutationsPerBatch,
            maximumReports = MaximumPlayerReportsPerBatch, ledger = Ledger.CaptureReplayState(), statuses = Statuses.CaptureReplayState(),
            attacks = Attacks.CaptureReplayState(), admissions = StatusDamageAdmissions.CaptureReplayState(), processed = ProcessedEvents.CaptureReplayState(),
            identities = ClientIdentities.CaptureReplayState(), batches = BatchSequences.CaptureReplayState(),
            eventIds = (serverEventIds as SequentialCombatEventIdSource)?.CaptureReplayState(),
            deaths = enemyDeaths.OrderBy(p => p.Key).Select(p => p.Value).ToArray(), retired = retiredEnemies.OrderBy(x => x).ToArray(),
            disconnected = disconnectedPlayers.OrderBy(x => x).ToArray() };
        public static ServerCombatGateway RestoreReplayState(GatewayReplayState s)
        {
            if (s.version != 1 || !s.supported) throw new InvalidOperationException("Unsupported gateway checkpoint/event allocator.");
            var ledger = CombatLedger.RestoreReplayState(s.ledger);
            var result = new ServerCombatGateway(ledger, SequentialCombatEventIdSource.RestoreReplayState(s.eventIds)) {
                Round = s.round, serverSequence = s.serverSequence, CombatStopped = s.stopped,
                MaximumResultsPerBatch = s.maximumResults, MaximumStatusMutationsPerBatch = s.maximumMutations, MaximumPlayerReportsPerBatch = s.maximumReports,
                Statuses = ServerStatusRegistry.RestoreReplayState(ledger, s.statuses), Attacks = ServerAttackRegistry.RestoreReplayState(s.attacks),
                StatusDamageAdmissions = ServerStatusDamageAdmissions.RestoreReplayState(s.admissions),
                ProcessedEvents = ProcessedEventCache.RestoreReplayState(s.processed), ClientIdentities = ClientEventIdentityRegistry.RestoreReplayState(s.identities),
                BatchSequences = ClientBatchSequenceTracker.RestoreReplayState(s.batches) };
            result.BindEvidence();
            foreach (var d in s.deaths) result.enemyDeaths.Add(d.TargetEntityId, d);
            foreach (var d in s.retired) result.retiredEnemies.Add(d);
            foreach (var d in s.disconnected) result.disconnectedPlayers.Add(d);
            return result;
        }
    }
    [Serializable] public struct ReplicaKillReplayState { public uint target, version; }
    [Serializable] public struct ReplicaControllerReplayState { public uint target; public StatusControllerReplayState state; }
    [Serializable] public sealed class ReplicaReplayState
    {
        public int version = 1;
        public CanonicalEntityState[] entities;
        public CanonicalStatusState[] statuses;
        public System.Collections.Generic.KeyValuePair<ulong, uint>[] targets;
        public ReplicaKillReplayState[] kills;
        public ReplicaControllerReplayState[] controllers;
    }
    public sealed partial class CanonicalWorldReplica
    {
        public StatusController GetReplayController(uint id) => statusControllers.TryGetValue(id, out var controller) ? controller : null;
        public ReplicaReplayState CaptureReplayState() => new ReplicaReplayState {
            entities = entities.OrderBy(p => p.Key).Select(p => p.Value).ToArray(),
            statuses = canonicalStatuses.OrderBy(p => p.Key.Value).Select(p => p.Value).ToArray(),
            targets = statusTargets.OrderBy(p => p.Key.Value).Select(p => new System.Collections.Generic.KeyValuePair<ulong, uint>(p.Key.Value, p.Value)).ToArray(),
            kills = confirmedKills.OrderBy(k => k.Target).ThenBy(k => k.Version).Select(k => new ReplicaKillReplayState { target = k.Target, version = k.Version }).ToArray(),
            controllers = statusControllers.OrderBy(p => p.Key).Select(p => new ReplicaControllerReplayState { target = p.Key, state = p.Value.CaptureReplayState() }).ToArray() };
        public static CanonicalWorldReplica RestoreReplayState(ReplicaReplayState s, Action<StatusTick> ticks = null)
        {
            if (s.version != 1) throw new InvalidOperationException("Unsupported replica checkpoint.");
            var result = new CanonicalWorldReplica();
            foreach (var e in s.entities) result.entities.Add(e.EntityId, e);
            foreach (var e in s.statuses) result.canonicalStatuses.Add(new StatusInstanceId(e.InstanceId), e);
            foreach (var e in s.targets) result.statusTargets.Add(new StatusInstanceId(e.Key), e.Value);
            foreach (var e in s.kills) result.confirmedKills.Add((e.target, e.version));
            foreach (var e in s.controllers) result.statusControllers.Add(e.target, StatusController.RestoreReplayState(e.state, ticks ?? (_ => { })));
            return result;
        }
    }
}
