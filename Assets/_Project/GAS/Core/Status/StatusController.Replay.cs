using System;
using System.Collections.Generic;
using System.Linq;

namespace MonsterSupergroup.GAS
{
    [Serializable] public sealed class EventSequenceState
    {
        private ushort sourceSlot, connectionEpoch;
        private uint nextSequence;
        private bool frozen;
        // Newly deserialized DTOs remain writable. Only internally cached capture values are frozen.
        public ushort slot { get => sourceSlot; set { RequireWritable(); sourceSlot = value; } }
        public ushort epoch { get => connectionEpoch; set { RequireWritable(); connectionEpoch = value; } }
        public uint next { get => nextSequence; set { RequireWritable(); nextSequence = value; } }
        internal void Freeze() => frozen = true;
        private void RequireWritable() { if (frozen) throw new InvalidOperationException("CachedReplaySnapshotIsReadOnly"); }
    }
    public sealed partial class SequentialCombatEventIdSource
    {
        public EventSequenceState CaptureReplayState() => new EventSequenceState { slot = sourceSlot, epoch = connectionEpoch, next = nextSequence };
        internal bool MatchesReplayState(EventSequenceState state) => state != null && state.slot == sourceSlot && state.epoch == connectionEpoch && state.next == nextSequence;
        public static SequentialCombatEventIdSource RestoreReplayState(EventSequenceState s)
        { var result = new SequentialCombatEventIdSource(s.slot, s.epoch); result.nextSequence = s.next; return result; }
    }
    public sealed partial class SequentialStatusInstanceIdSource
    {
        public EventSequenceState CaptureReplayState() => new EventSequenceState { slot = sourceSlot, epoch = connectionEpoch, next = nextSequence };
        internal bool MatchesReplayState(EventSequenceState state) => state != null && state.slot == sourceSlot && state.epoch == connectionEpoch && state.next == nextSequence;
        public static SequentialStatusInstanceIdSource RestoreReplayState(EventSequenceState s)
        { var result = new SequentialStatusInstanceIdSource(s.slot, s.epoch); result.nextSequence = s.next; return result; }
    }
    public sealed partial class CombatEventStatusInstanceIdSource
    {
        public EventSequenceState CaptureReplayState() => eventIds is SequentialCombatEventIdSource s ? s.CaptureReplayState() : null;
        internal bool MatchesReplayState(EventSequenceState state) => eventIds is SequentialCombatEventIdSource s ? s.MatchesReplayState(state) : state == null;
    }
    [Serializable] public struct StatusActiveReplayState
    {
        public StatusInstance instance;
        public int canonicalStack, predictedDelta, remainingHits, completedTicks;
        public uint canonicalVersion;
        public float elapsed;
    }
    [Serializable] public struct StatusWatermarkReplayState { public ulong id; public uint version; }
    [Serializable] public sealed class StatusControllerReplayState
    {
        public int version = 1;
        public double time;
        public EventSequenceState ids;
        public bool supported, eventIds, executeAll, offline, server;
        public uint localPlayer, targetOwner;
        public StatusActiveReplayState[] active, completed;
        public StatusWatermarkReplayState[] removals, removedApplications;
    }
    [Serializable] public sealed class StatusReplayBoundary
    {
        private EventSequenceState sequence;
        private bool usesEvents, isSupported, executesAll, isOffline, isServer, frozen;
        private uint player, owner;
        public EventSequenceState ids { get => sequence; set { RequireWritable(); sequence = value; } }
        public bool eventIds { get => usesEvents; set { RequireWritable(); usesEvents = value; } }
        public bool supported { get => isSupported; set { RequireWritable(); isSupported = value; } }
        public bool executeAll { get => executesAll; set { RequireWritable(); executesAll = value; } }
        public bool offline { get => isOffline; set { RequireWritable(); isOffline = value; } }
        public bool server { get => isServer; set { RequireWritable(); isServer = value; } }
        public uint localPlayer { get => player; set { RequireWritable(); player = value; } }
        public uint targetOwner { get => owner; set { RequireWritable(); owner = value; } }
        internal void Freeze() { sequence?.Freeze(); frozen = true; }
        private void RequireWritable() { if (frozen) throw new InvalidOperationException("CachedReplaySnapshotIsReadOnly"); }
    }
    public sealed partial class StatusController
    {
        private StatusReplayBoundary capturedReplayBoundary;
        public int TransferTo(StatusController target, uint targetEntityId)
        {
            if (!CombatEvidence.Enabled) return EvidenceCore_TransferTo(target, targetEntityId);
            using var evidence = CombatEvidence.Begin(this, "status", "TransferTo", new object[] { targetEntityId, target?.CaptureReplayState() }, o => ((StatusController)o).CaptureReplayState());
            int result = EvidenceCore_TransferTo(target, targetEntityId);
            evidence.Complete(new { result, targetState = target?.CaptureReplayState() }); return result;
        }
        public StatusReplayBoundary CaptureReplayBoundary()
        {
            var scope = executionPolicy as StatusExecutionScope;
            bool usesEvents = instanceIds is CombatEventStatusInstanceIdSource;
            bool executesAll = executionPolicy is ExecuteAllStatusPolicy;
            bool offline = scope?.IsOffline ?? false, server = scope?.IsServer ?? false;
            uint localPlayer = scope?.LocalPlayerId ?? 0, targetOwner = scope?.TargetOwnerPlayerId ?? 0;
            var prior = capturedReplayBoundary;
            if (prior != null && prior.eventIds == usesEvents && prior.executeAll == executesAll &&
                prior.offline == offline && prior.server == server && prior.localPlayer == localPlayer && prior.targetOwner == targetOwner &&
                // A different unsupported policy must not acquire the supported status of an empty-valued scope.
                prior.supported == (prior.ids != null && (scope != null || executesAll)) &&
                MatchesBoundarySequence(prior.ids)) return prior;
            var ids = instanceIds is SequentialStatusInstanceIdSource sequential ? sequential.CaptureReplayState()
                : instanceIds is CombatEventStatusInstanceIdSource events ? events.CaptureReplayState() : null;
            var result = new StatusReplayBoundary { ids = ids, eventIds = usesEvents,
                supported = ids != null && (scope != null || executionPolicy is ExecuteAllStatusPolicy),
                executeAll = executesAll, offline = offline, server = server, localPlayer = localPlayer, targetOwner = targetOwner };
            result.Freeze();
            return capturedReplayBoundary = result;
        }
        private bool MatchesBoundarySequence(EventSequenceState state) => instanceIds switch {
            SequentialStatusInstanceIdSource sequential => sequential.MatchesReplayState(state),
            CombatEventStatusInstanceIdSource events => events.MatchesReplayState(state),
            _ => state == null
        };
        public void RestoreReplayBoundary(StatusReplayBoundary s)
        {
            if (!s.supported) throw new InvalidOperationException("Unsupported status boundary.");
            instanceIds = s.eventIds ? new CombatEventStatusInstanceIdSource(SequentialCombatEventIdSource.RestoreReplayState(s.ids)) : SequentialStatusInstanceIdSource.RestoreReplayState(s.ids);
            executionPolicy = s.executeAll ? ExecuteAllStatusPolicy.Instance : new StatusExecutionScope(s.offline, s.server, s.localPlayer, s.targetOwner);
        }
        public StatusControllerReplayState CaptureReplayState()
        {
            var scope = executionPolicy as StatusExecutionScope;
            var ids = instanceIds is SequentialStatusInstanceIdSource sequential ? sequential.CaptureReplayState()
                : instanceIds is CombatEventStatusInstanceIdSource events ? events.CaptureReplayState() : null;
            return new StatusControllerReplayState { time = currentTime, ids = ids, eventIds = instanceIds is CombatEventStatusInstanceIdSource,
                supported = ids != null && (scope != null || executionPolicy is ExecuteAllStatusPolicy),
                executeAll = executionPolicy is ExecuteAllStatusPolicy, offline = scope?.IsOffline ?? false, server = scope?.IsServer ?? false,
                localPlayer = scope?.LocalPlayerId ?? 0, targetOwner = scope?.TargetOwnerPlayerId ?? 0,
                active = activeStatuses.OrderBy(p => p.Key).SelectMany(p => p.Value).Select(CaptureActive).ToArray(),
                completed = completedStatuses.OrderBy(p => p.Key.Value).Select(p => CaptureActive(p.Value)).ToArray(),
                removals = removalVersions.OrderBy(p => p.Key.Value).Select(p => new StatusWatermarkReplayState { id = p.Key.Value, version = p.Value }).ToArray(),
                removedApplications = removedApplications.OrderBy(p => p.Key.Value).Select(p => new StatusWatermarkReplayState { id = p.Key.Value, version = p.Value }).ToArray() };
        }
        private static StatusActiveReplayState CaptureActive(ActiveStatus a) => new StatusActiveReplayState {
            instance = a.Instance, canonicalStack = a.CanonicalStack, predictedDelta = a.PredictedStackDelta,
            canonicalVersion = a.CanonicalVersion, remainingHits = a.RemainingHits, completedTicks = a.CompletedTicks, elapsed = a.Elapsed };
        private static ActiveStatus RestoreActive(StatusActiveReplayState a) => ActiveStatus.Restore(a);
        public static StatusController RestoreReplayState(StatusControllerReplayState s, Action<StatusTick> receiver)
        {
            if (s.version != 1 || !s.supported) throw new InvalidOperationException("Unsupported status replay checkpoint/policy.");
            IStatusInstanceIdSource ids = s.eventIds ? new CombatEventStatusInstanceIdSource(SequentialCombatEventIdSource.RestoreReplayState(s.ids))
                : SequentialStatusInstanceIdSource.RestoreReplayState(s.ids);
            IStatusExecutionPolicy policy = s.executeAll ? ExecuteAllStatusPolicy.Instance : new StatusExecutionScope(s.offline, s.server, s.localPlayer, s.targetOwner);
            var result = new StatusController(receiver, ids, policy) { currentTime = s.time };
            foreach (var a in s.active)
            {
                if (!result.activeStatuses.TryGetValue(a.instance.DefinitionId, out var list)) result.activeStatuses.Add(a.instance.DefinitionId, list = new List<ActiveStatus>());
                list.Add(RestoreActive(a));
            }
            foreach (var a in s.completed) result.completedStatuses.Add(a.instance.InstanceId, RestoreActive(a));
            foreach (var a in s.removals) result.removalVersions.Add(new StatusInstanceId(a.id), a.version);
            foreach (var a in s.removedApplications) result.removedApplications.Add(new StatusInstanceId(a.id), a.version);
            return result;
        }
    }
}
