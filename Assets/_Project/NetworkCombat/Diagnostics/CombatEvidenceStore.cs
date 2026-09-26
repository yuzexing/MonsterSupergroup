using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    public enum EvidenceProfile { Standard, Diagnostic }

    public sealed class EvidenceStoreOptions
    {
        public EvidenceProfile Profile;
        public long SessionBytes = 8L << 30, TotalBytes = 32L << 30;
        public int QueueBytes = 32 << 20, ReservedBytes = 4 << 20, SegmentBytes = 32 << 20;
        public double SegmentSeconds = 60;
        public IEvidenceStorage Storage = new FileEvidenceStorage();
        public DiagnosticMemoryBudget Memory = new();
        public bool ObserveQueue;
        public bool DeferObservationWindows;
        public long ObservationOriginTicks;
        public int ObservationWindowMilliseconds = EvidenceQueueObservation.WindowMilliseconds;

        public static EvidenceStoreOptions ForProfile(EvidenceProfile profile, bool observeQueue = false)
        {
            if (profile != EvidenceProfile.Standard && profile != EvidenceProfile.Diagnostic)
                throw new ArgumentOutOfRangeException(nameof(profile));
            bool diagnostic = profile == EvidenceProfile.Diagnostic;
            return new EvidenceStoreOptions {
                Profile = profile, QueueBytes = diagnostic ? 512 << 20 : 32 << 20,
                Memory = new DiagnosticMemoryBudget(diagnostic ? 768L << 20 : 128L << 20),
                ObserveQueue = observeQueue, DeferObservationWindows = observeQueue,
                ObservationWindowMilliseconds = diagnostic ? 1000 : EvidenceQueueObservation.WindowMilliseconds };
        }
    }

    [Serializable]
    public sealed class EvidenceConfiguration
    {
        public string profile, drainPolicy;
        public long queueBytes, reservedBytes, memoryBudgetBytes;
        public int windowMilliseconds;
        internal EvidenceConfiguration(EvidenceStoreOptions options)
        {
            profile = options.Profile == EvidenceProfile.Diagnostic ? "diagnostic" : "standard";
            drainPolicy = options.Profile == EvidenceProfile.Diagnostic ? "WaitForCompletion" : "Bounded30Seconds";
            queueBytes = options.QueueBytes; reservedBytes = options.ReservedBytes;
            memoryBudgetBytes = options.Memory.Limit; windowMilliseconds = options.ObservationWindowMilliseconds;
        }
    }

    [Serializable]
    public sealed class EvidenceCoverage
    {
        public int schemaVersion = 2;
        public string captureId, runId, written = "0", flushed = "0", failure;
        private ulong producedSequence;
        public string produced { get => producedSequence.ToString(); set => producedSequence = ulong.Parse(value); }
        [Newtonsoft.Json.JsonIgnore] internal ulong Produced { get => producedSequence; set => producedSequence = Math.Max(producedSequence, value); }
        public uint round;
        public bool complete, tailUnknown = true;
        public long dropped, criticalDropped, observationDropped, queuedBytes;
        public long coverageRevision, failureEpoch;
        public string failureFirstSequence, failureLastSequence, reliableFromSequence;
        public bool recoveryPending;
        // Version zero preserves legacy deserialization; the first observed failure starts a complete v1 history.
        public int integrityHistoryVersion;
        public List<EvidenceIntegrityEpisode> integrityHistory = new();
        [Newtonsoft.Json.JsonIgnore] internal string recoveryCheckpoint;
        [Newtonsoft.Json.JsonIgnore] internal long recoveryCheckpointEpoch;
        public List<EvidenceGap> gaps = new();
    }
    [Serializable] public sealed class EvidenceIntegrityEpisode
    {
        private ulong? firstSequence, lastSequence;
        public string first { get => firstSequence?.ToString(); set => firstSequence = value == null ? null : ulong.Parse(value); }
        public string last { get => lastSequence?.ToString(); set => lastSequence = value == null ? null : ulong.Parse(value); }
        [Newtonsoft.Json.JsonIgnore] internal ulong? First { get => firstSequence; set => firstSequence = value; }
        [Newtonsoft.Json.JsonIgnore] internal ulong? Last { get => lastSequence; set => lastSequence = value; }
        public long epoch;
        public string reliableFromSequence;
        public bool conservative, tailMarker;
    }
    [Serializable] public sealed class EvidenceGap
    {
        private ulong begin, end;
        public string first { get => begin.ToString(); set => begin = ulong.Parse(value); }
        public string last { get => end.ToString(); set => end = ulong.Parse(value); }
        internal ulong First => begin;
        internal ulong Last => end;
        public string reason, engine, eventId, stage;
        public long count;
        public bool conservative;
    }
    [Serializable] public sealed class EvidenceFile { public string path, revision; public long length; }

    /// <summary>Single writer owns files. Network, gameplay and exception callbacks only enqueue bounded work.</summary>
    public sealed partial class CombatEvidenceStore : IDisposable
    {
        public const int MaximumBlockBytes = 64 * 1024;
        private readonly EvidenceStoreOptions options;
        private readonly Queue<(int bytes, Action action, AdvanceBlock advances, long queuedAt)> queue = new();
        private readonly object gate = new();
        private readonly object filesGate = new();
        private readonly AutoResetEvent wake = new(false);
        private readonly Thread worker;
        private readonly Dictionary<string, Source> sources = new();
        private readonly Dictionary<string, EvidenceCoverage> health = new();
        private readonly Dictionary<string, long> usage = new();
        private readonly SortedDictionary<string, EvidenceFile> durableFiles = new(StringComparer.Ordinal);
        private long catalogBytes;
        private long totalUsage = -1;
        private bool stopping, closed;
        private long pendingBytes, dropped, writeTicks;
        private double nextFlush, nextPrune;
        private long peakPendingBytes;
        private const int CodecWorkspaceBytes = 24 << 20;
        public EvidenceStageMetrics Metrics { get; } = new();
        public string Root { get; }
        public string LastFailure { get; private set; }
        public long PendingBytes { get { lock (gate) return pendingBytes; } }
        public long Dropped => Interlocked.Read(ref dropped);
        public double WriteMilliseconds => Interlocked.Read(ref writeTicks) * 1000d / System.Diagnostics.Stopwatch.Frequency;
        public bool Closed => Volatile.Read(ref closed);
        private int persistedRecord;
        public bool HasPersistedRecords => Volatile.Read(ref persistedRecord) != 0;
        public DiagnosticMemoryBudget Memory => options.Memory;
        public EvidenceProfile Profile => options.Profile;
        public EvidenceConfiguration Configuration => new(options);
        public long PeakPendingBytes => Interlocked.Read(ref peakPendingBytes);
        public EvidenceQueueObservation QueueObservation { get; }
        public bool ObservationRequested => options.ObserveQueue;
        public string ObservationUnavailableReason { get; }

        public CombatEvidenceStore(string root, EvidenceStoreOptions options = null)
        {
            Root = Path.GetFullPath(root);
            if (Environment.OSVersion.Platform == PlatformID.Win32NT && !Root.StartsWith(@"\\?\", StringComparison.Ordinal))
                Root = Root.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + Root.Substring(2) : @"\\?\" + Root;
            this.options = options ?? new EvidenceStoreOptions();
            if (!Memory.TryReserve(CodecWorkspaceBytes)) throw new InvalidOperationException("Insufficient diagnostic codec budget.");
            try
            {
                if (this.options.ObserveQueue)
                {
                    if (EvidenceQueueObservation.TryCreate(Memory, true, out var observation, this.options.ObservationOriginTicks, this.options.DeferObservationWindows, this.options.ObservationWindowMilliseconds)) QueueObservation = observation;
                    else ObservationUnavailableReason = "QueueObservationBudgetUnavailable";
                }
                worker = new Thread(Consume) { IsBackground = true, Name = "Combat evidence disk" };
                worker.Start();
            }
            catch { QueueObservation?.ReleaseAfterStop(true, true); Memory.Release(CodecWorkspaceBytes); throw; }
        }

        public bool TryWrite(DiagnosticRecord record) => TryWrite(record, true);
        public bool TryWrite(DiagnosticRecord record, bool freezePayload, Func<object> capture = null)
        {
            string key;
            using (DiagnosticMainTiming.Measure(DiagnosticMainStage.SinkMetadata))
                key = SourceKey(record.runId, record.round, record.captureId);
            long lockStarted = EvidenceStageMetrics.Now;
            var producerWaiting = DiagnosticMainTiming.Measure(DiagnosticMainStage.ProducerGateWait);
            bool waitEnded = false;
            try
            {
                lock (gate)
                {
                    producerWaiting.Dispose(); waitEnded = true;
                    using var gateWork = DiagnosticMainTiming.Measure(DiagnosticMainStage.ProducerGateWork);
                    Metrics.LockWait(lockStarted);
                    QueueObservation?.Attempt(EvidenceQueueEntry.TryWrite, EvidenceStageMetrics.Now);
                    activeAdvances = null;
                    if (!health.TryGetValue(key, out var state)) health.Add(key, state = new EvidenceCoverage {
                        captureId = record.captureId, runId = record.runId, round = record.round });
                    state.produced = record.recordSequence;
                    int bytes = Math.Max(512, record.estimatedBytes);
                    long queueLimit = options.QueueBytes - (record.critical ? 0 : options.ReservedBytes);
                    bool capturedCheckpoint = capture != null &&
                        (record.stage == "replay.checkpoint" || record.stage == "replay.engine_checkpoint");
                    // Reserve the bounded capture workspace in Memory, then charge only the captured value
                    // to the queue. A small recovery checkpoint must not need 8 MiB of free queue space.
                    if (!Admit(EvidenceQueueEntry.TryWrite, bytes, queueLimit, true,
                        state, state.Produced, QueueObservation == null ? (int?)null : record.stage == "replay.input" ? 0 : record.stage == "replay.output" ? (record.outcome == "Completed" ? 1 : 2) : (int?)null, record.stage,
                        capturedCheckpoint ? 512 : -1))
                    {
                        CountDropped(state, record.stage == "observation.snapshot" || record.stage == "performance.snapshot");
                        Gap(state, record.recordSequence, "QueueOverload"); return false;
                    }
                    List<IDisposable> leases = null;
                    try
                    {
                        using (DiagnosticMainTiming.Measure(DiagnosticMainStage.SinkMetadata)) record = record.Copy();
                        if (capture != null)
                        {
                            long captureStarted = EvidenceStageMetrics.Now;
                            try { using (DiagnosticMainTiming.Measure(DiagnosticMainStage.Capture)) record.input = capture(); }
                            finally { Metrics.Capture(captureStarted); }
                            int retained;
                            using (DiagnosticMainTiming.Measure(DiagnosticMainStage.RetainedSize))
                                retained = record.stage == "observation.snapshot" ? bytes :
                                    (int)Math.Min(int.MaxValue, 512L + CombatEvidenceRuntime.RetainedBytes(record.input));
                            if (retained > bytes) throw new InvalidDataException("CaptureExceededReservedMemory");
                            Memory.Release(bytes - retained); bytes = retained; record.estimatedBytes = retained;
                            if (capturedCheckpoint)
                            {
                                EvidenceQueueGuard? guard = stopping ? EvidenceQueueGuard.Stopping :
                                    bytes > options.QueueBytes ? EvidenceQueueGuard.OversizedRecord :
                                    pendingBytes + bytes > queueLimit ? EvidenceQueueGuard.QueueLimit : (EvidenceQueueGuard?)null;
                                if (guard.HasValue)
                                {
                                    int requested = bytes; Memory.Release(bytes); bytes = 0;
                                    ObserveAdmissionRejection(EvidenceQueueEntry.TryWrite, guard.Value, requested, queueLimit,
                                        state, state.Produced, null, record.stage);
                                    CountDropped(state); Gap(state, record.recordSequence, "QueueOverload"); return false;
                                }
                            }
                        }
                        if (freezePayload)
                        {
                            long freezeStarted = EvidenceStageMetrics.Now;
                            try
                            {
                                using var freezing = DiagnosticMainTiming.Measure(DiagnosticMainStage.Freeze);
                                record.input = DiagnosticPayload.Freeze(record.input); record.before = DiagnosticPayload.Freeze(record.before); record.after = DiagnosticPayload.Freeze(record.after);
                            }
                            finally { Metrics.Freeze(freezeStarted); }
                        }
                        using (DiagnosticMainTiming.Measure(DiagnosticMainStage.SharedLeases)) leases = AcquireSharedLeases(record);
                        pendingBytes += bytes; peakPendingBytes = Math.Max(peakPendingBytes, pendingBytes);
                        // The checkpoint describes the main-thread capture moment, not its later disk append.
                        long checkpointEpoch = IsRecoveryCheckpoint(record) ? state.failureEpoch : -1;
                        queue.Enqueue((bytes, () => {
                            BindRecordService(record);
                            long waitStarted = QueueObservation == null ? 0 : EvidenceStageMetrics.Now;
                            var waiting = EvidenceServiceTiming.Measure(EvidenceServiceStage.FilesGateWait);
                            try { lock (filesGate) { waiting.Dispose(); QueueObservation?.Span(EvidenceQueueStage.FilesGateWait, waitStarted, EvidenceStageMetrics.Now); Append(key, record, checkpointEpoch, leases != null); } }
                            finally { ReleaseSharedLeases(leases); }
                        }, null, EvidenceStageMetrics.Now));
                        QueueObservation?.Enqueued(bytes, pendingBytes, EvidenceStageMetrics.Now);
                        QueueObservation?.Accepted(EvidenceQueueEntry.TryWrite, EvidenceStageMetrics.Now);
                    }
                    catch (Exception error)
                    {
                        ReleaseSharedLeases(leases);
                        Memory.Release(bytes);
                        QueueObservation?.CaptureFailed(EvidenceQueueEntry.TryWrite, EvidenceStageMetrics.Now);
                        record.reason = record.stage + ":" + error.GetType().Name;
                        ReportCaptureFailure(record);
                        return false;
                    }
                }
            }
            finally { if (!waitEnded) producerWaiting.Dispose(); }
            using (DiagnosticMainTiming.Measure(DiagnosticMainStage.Wake)) wake.Set();
            return true;
        }

        public bool Schedule(int retainedBytes, Action action)
        {
            lock (gate)
            {
                activeAdvances = null;
                QueueObservation?.Attempt(EvidenceQueueEntry.Schedule, EvidenceStageMetrics.Now);
                if (!Admit(EvidenceQueueEntry.Schedule, retainedBytes, options.QueueBytes - options.ReservedBytes, false)) return false;
                pendingBytes += retainedBytes; peakPendingBytes = Math.Max(peakPendingBytes, pendingBytes); queue.Enqueue((retainedBytes, action, null, EvidenceStageMetrics.Now));
                QueueObservation?.Enqueued(retainedBytes, pendingBytes, EvidenceStageMetrics.Now);
                QueueObservation?.Accepted(EvidenceQueueEntry.Schedule, EvidenceStageMetrics.Now);
            }
            wake.Set(); return true;
        }

        // Called under gate. Capture workspace can exceed the minimum queue entry without exceeding Memory.
        private bool Admit(EvidenceQueueEntry entry, int bytes, long queueLimit, bool checkOversized,
            EvidenceCoverage state = null, ulong? sequence = null, int? phase = null, string stage = null, int queueChargeBytes = -1)
        {
            using var admission = DiagnosticMainTiming.Measure(DiagnosticMainStage.Admission);
            EvidenceQueueGuard guard;
            long budgetBefore = 0;
            int queueCharge = queueChargeBytes < 0 ? bytes : queueChargeBytes;
            if (stopping) guard = EvidenceQueueGuard.Stopping;
            else if (checkOversized && queueCharge > options.QueueBytes) guard = EvidenceQueueGuard.OversizedRecord;
            else if (pendingBytes + queueCharge > queueLimit) guard = EvidenceQueueGuard.QueueLimit;
            else if (!Memory.TryReserve(bytes, out budgetBefore)) guard = EvidenceQueueGuard.BudgetReservation;
            else return true;
            ObserveAdmissionRejection(entry, guard, guard == EvidenceQueueGuard.BudgetReservation ? bytes : queueCharge,
                queueLimit, state, sequence, phase, stage, budgetBefore);
            return false;
        }

        private void ObserveAdmissionRejection(EvidenceQueueEntry entry, EvidenceQueueGuard guard, int requestedBytes,
            long queueLimit, EvidenceCoverage state, ulong? sequence, int? phase, string stage, long budgetBefore = 0)
        {
            if (QueueObservation != null)
            {
                QueueObservation.Pending(pendingBytes, EvidenceStageMetrics.Now);
                EvidenceQueueRejection rejection = default;
                if (QueueObservation.ShouldCaptureFirst(entry, guard))
                {
                    if (guard != EvidenceQueueGuard.BudgetReservation) budgetBefore = Memory.Used;
                    ulong.TryParse(state?.written, out ulong written); ulong.TryParse(state?.flushed, out ulong flushed);
                    rejection = new EvidenceQueueRejection { runId = state?.runId, captureId = state?.captureId, round = state?.round,
                        sequence = sequence, phase = phase, stage = stage, requestedBytes = requestedBytes, pendingBytes = pendingBytes,
                        effectiveQueueLimit = queueLimit, budgetUsed = budgetBefore, budgetLimit = Memory.Limit,
                        produced = state?.Produced, written = state == null ? (ulong?)null : written, flushed = state == null ? (ulong?)null : flushed };
                }
                QueueObservation.Rejected(entry, guard, in rejection, EvidenceStageMetrics.Now);
            }
        }

        public bool ExportQueueObservation(string path)
        {
            lock (gate)
            {
                if (!stopping || worker.IsAlive) return false;
                return QueueObservation != null && QueueObservation.StopAndExport(path, true, true);
            }
        }

        // The observer's live mirror is independent of writer-owned arrays. Never hold gate during I/O.
        public bool ExportPartialQueueObservation(string path) =>
            QueueObservation != null && QueueObservation.ExportPartial(path, Volatile.Read(ref stopping));

        private static string SourceKey(string run, uint round, string capture) =>
            SafePart(run ?? "boot") + "/" + round + "/sources/" + SafePart(capture);
        private static string SafePart(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 100 || value.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
                throw new InvalidDataException("Invalid evidence identity.");
            return value;
        }
        public string Resolve(string relative)
        {
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.Contains(":") || relative.Split('/', '\\').Any(p => p == ".." || p.Length == 0))
                throw new InvalidDataException("Invalid evidence path.");
            string result = Path.GetFullPath(Path.Combine(Root, relative));
            if (!result.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Evidence path escaped root.");
            return result;
        }

        private static bool IsRecoveryCheckpoint(DiagnosticRecord record) => record.stage == "replay.checkpoint" &&
            record.input is ReplayCheckpointSet set && set.engines != null && set.engines.Length > 0 &&
            set.engines.All(engine => engine != null && engine.state != null && !string.IsNullOrEmpty(engine.engine));

        private void BindRecordService(DiagnosticRecord record)
        {
            if (QueueObservation == null) return;
            ulong.TryParse(record.recordSequence, out ulong sequence);
            QueueObservation.BindServiceIdentity(new EvidenceServiceIdentity {
                kind = EvidenceServiceWorkKind.Record, runId = record.runId, captureId = record.captureId, round = record.round,
                firstSequence = sequence, lastSequence = sequence, logicalCount = 1, engine = record.engine,
                operation = record.operation, stage = record.stage,
                phase = record.stage == "replay.input" ? 0 : record.stage == "replay.output" ? (record.outcome == "Completed" ? 1 : 2) : -1 });
        }

        private void Append(string key, DiagnosticRecord record, long checkpointEpoch = -1, bool hasSharedPayloads = false)
        {
            using var appending = EvidenceServiceTiming.Measure(EvidenceServiceStage.Append);
            EvidenceCoverage state; lock (gate) state = health[key];
            try
            {
                if (!sources.TryGetValue(key, out var source))
                {
                    using var opening = EvidenceServiceTiming.Measure(EvidenceServiceStage.SourceOpen);
                    string directory = Resolve(key);
                    using (EvidenceServiceTiming.Measure(EvidenceServiceStage.DirectoryCreate)) Directory.CreateDirectory(directory);
                    Recover(directory);
                    // Closing a previous context frees its handles and makes the ended round eligible for retention.
                    foreach (var previous in sources.Where(p => p.Key.EndsWith("/sources/" + record.captureId, StringComparison.Ordinal) && p.Key != key).ToArray())
                    {
                        try
                        {
                            using var closing = EvidenceServiceTiming.Measure(EvidenceServiceStage.PreviousSourceClose);
                            previous.Value.Flush(true); IndexFile(previous.Value.Path); previous.Value.Dispose();
                            string closedCoverage;
                            lock (gate)
                            {
                                var ended = health[previous.Key]; ended.written = previous.Value.LastSequence; ended.flushed = ended.written;
                                PublishRecovery(ended); ended.tailUnknown = false;
                                ended.complete = ended.dropped == 0 && ended.failure == null;
                                ended.coverageRevision++; closedCoverage = EvidenceJson.Encode(ended);
                            }
                            string closedPath = Path.Combine(previous.Value.Directory, "coverage.json");
                            EvidenceJson.AtomicWrite(closedPath, closedCoverage); IndexFile(closedPath);
                        }
                        catch (Exception error) { FailSource(previous.Key, error); }
                        sources.Remove(previous.Key);
                    }
                    sources.Add(key, source = new Source(directory, options.Storage, bytes => Reserve(directory, bytes), Metrics, QueueObservation));
                    string session = Directory.GetParent(Directory.GetParent(directory).FullName).FullName;
                    using var manifest = EvidenceServiceTiming.Measure(EvidenceServiceStage.Manifest);
                    if (!File.Exists(Path.Combine(session, "manifest.json"))) EvidenceJson.AtomicWrite(Path.Combine(session, "manifest.json"),
                        EvidenceJson.Encode(new { schemaVersion = 2, runId = record.runId, round = record.round, capacityBytes = options.SessionBytes,
                            completeness = "Read each source coverage and this machine's replication watermarks; absent records are not proof of non-execution." }));
                }
                double now = Seconds;
                bool checkpoint = record.stage == "replay.checkpoint";
                // Begin a retained interval at its checkpoint. Old intervals may then be removed independently.
                if (source.Writer == null || source.Bytes >= options.SegmentBytes || now - source.Opened >= options.SegmentSeconds || checkpoint)
                {
                    using var rotating = EvidenceServiceTiming.Measure(EvidenceServiceStage.Rotation);
                    if (source.Path != null) { source.Flush(true); IndexFile(source.Path); }
                    source.Rotate(now, record.recordSequence);
                }
                if (record.input is AdvanceBlock advances)
                {
                    source.AppendAdvances(advances);
                    lock (gate) state.written = source.LastSequence;
                    return;
                }
                if (hasSharedPayloads) MaterializeSharedPayloads(source, record);
                if (record.input != null)
                {
                    long encodeStarted = EvidenceStageMetrics.Now;
                    string payload;
                    try { using (EvidenceServiceTiming.Measure(EvidenceServiceStage.InputJson)) payload = EvidenceJson.EncodeBounded(record.input); } finally { Metrics.Encoding(encodeStarted); }
                    if (record.schemaVersion >= 2 && payload.Contains("\"KnockbackSettings\""))
                    {
                        var shared = Newtonsoft.Json.Linq.JToken.Parse(payload); ShareKnockbackSettings(source, shared);
                        record.input = shared; payload = EvidenceJson.EncodeBounded(shared);
                    }
                    if (ShouldExternalizePayload(record, Encoding.UTF8.GetByteCount(payload)))
                    {
                        bool checkpointPayload = checkpoint || record.stage == "replay.engine_checkpoint";
                        string folder = checkpointPayload ? "checkpoints" : "inputs";
                        string relative = SavePayload(source, folder, payload);
                        if (checkpointPayload) record.checkpointRef = relative; else record.inputRef = relative;
                        record.input = null;
                    }
                }
                source.Append(record, now);
                lock (gate)
                {
                    state.written = source.LastSequence;
                    if (checkpointEpoch >= 0 && state.recoveryPending && checkpointEpoch == state.failureEpoch)
                    { state.recoveryCheckpoint = record.recordSequence; state.recoveryCheckpointEpoch = checkpointEpoch; }
                }
            }
            catch (Exception error)
            {
                lock (gate) { Gap(state, record.recordSequence, error is EvidenceCapacityException ? "CapacityExceeded" : "WriteFailure"); state.failure = error.GetType().Name + ": " + error.Message; }
                LastFailure = state.failure;
                FailSource(key, error);
            }
        }

        private static void Gap(EvidenceCoverage state, string sequence, string reason)
        {
            ulong number = ulong.Parse(sequence);
            var last = state.gaps.Count > 0 ? state.gaps[state.gaps.Count - 1] : null;
            // Normal overload arrives monotonically: keep rejection bounded and allocation-light.
            if (last != null && number <= last.Last && state.gaps.Any(g => g.First <= number && number <= g.Last)) return;
            MarkRecoveryPending(state, number, number);
            if (last != null && last.reason == reason && last.Last != ulong.MaxValue && number == last.Last + 1)
            { last.last = sequence; last.count++; }
            else if (state.gaps.Count < 128) state.gaps.Add(new EvidenceGap { first = sequence, last = sequence, reason = reason, count = 1 });
            else
            {
                // A bounded conservative interval includes every later loss, even when successfully written records interleave.
                var range = state.gaps[127];
                ulong first = Math.Min(range.First, number);
                ulong end = Math.Max(range.Last, number);
                range.first = first.ToString(); range.last = end.ToString(); range.count++; range.conservative = true;
                range.reason = "CoalescedCaptureGap";
            }
        }

        private void Consume()
        {
            try
            {
            EvidenceWorkerProfiling.Start("writer", Root);
            QueueObservation?.BeginBackground(EvidenceServiceContext.Startup, EvidenceStageMetrics.Now);
            try { using (EvidenceServiceTiming.Measure(EvidenceServiceStage.Startup)) { lock (filesGate) { Directory.CreateDirectory(Root); RecoverExisting(); } } }
            catch (Exception error) { LastFailure = error.Message; RecordShutdownWorkerFailure(error); }
            finally { QueueObservation?.EndBackground(EvidenceStageMetrics.Now); }
            while (true)
            {
                (int bytes, Action action, AdvanceBlock advances, long queuedAt) work = default;
                bool collecting;
                lock (gate)
                {
                    collecting = !stopping && queue.Count == 1 && activeAdvances != null &&
                        activeAdvances.count < AdvanceBlock.Capacity && Seconds - activeAdvances.created < .1;
                    if (queue.Count != 0 && !collecting)
                    {
                        work = queue.Dequeue();
                        QueueObservation?.PublishDequeuedWork(work.queuedAt, work.bytes);
                        QueueObservation?.ConsumerPending(pendingBytes, EvidenceStageMetrics.Now);
                        if (ReferenceEquals(work.advances, activeAdvances)) activeAdvances = null;
                    }
                    else if (queue.Count == 0 && stopping) break;
                }
                if (work.action != null)
                {
                    long started = System.Diagnostics.Stopwatch.GetTimestamp();
                    QueueObservation?.BeginService(started);
                    if (work.advances != null) BindAdvanceService(work.advances);
                    Metrics.QueueWait(work.queuedAt);
                    QueueObservation?.Dequeued(started);
                    QueueObservation?.Span(EvidenceQueueStage.QueueResidence, work.queuedAt, started);
                    try { EvidenceWorkerProfiling.BeginWork(); work.action(); }
                    catch (Exception error) { LastFailure = error.GetType().Name + ": " + error.Message; RecordShutdownWorkerFailure(error); }
                    finally
                    {
                        var terminalWait = EvidenceServiceTiming.Measure(EvidenceServiceStage.TerminalGateWait);
                        lock (gate) {
                            terminalWait.Dispose();
                            using var terminalGate = EvidenceServiceTiming.Measure(EvidenceServiceStage.TerminalGate);
                            pendingBytes -= work.bytes; QueueObservation?.Pending(pendingBytes, EvidenceStageMetrics.Now); QueueObservation?.ConsumerPending(pendingBytes, EvidenceStageMetrics.Now);
                        }
                        using (EvidenceServiceTiming.Measure(EvidenceServiceStage.MemoryRelease)) Memory.Release(work.bytes);
                        long ended = System.Diagnostics.Stopwatch.GetTimestamp();
                        QueueObservation?.EndService(ended);
                        Interlocked.Add(ref writeTicks, ended - started);
                        QueueObservation?.Span(EvidenceQueueStage.Service, started, ended);
                        QueueObservation?.Completed(work.bytes, ended);
                        EvidenceWorkerProfiling.EndWork();
                    }
                }
                else
                {
                    long idle = QueueObservation == null ? 0 : EvidenceStageMetrics.Now;
                    wake.WaitOne(50);
                    QueueObservation?.Span(collecting ? EvidenceQueueStage.IdleCollecting : EvidenceQueueStage.IdleEmpty, idle, EvidenceStageMetrics.Now);
                }
                QueueObservation?.BeginBackground(EvidenceServiceContext.Maintenance, EvidenceStageMetrics.Now);
                EvidenceWorkerProfiling.BeginWork();
                try
                {
                    using var maintenance = EvidenceServiceTiming.Measure(EvidenceServiceStage.Maintenance);
                    long waiting = QueueObservation == null ? 0 : EvidenceStageMetrics.Now;
                    var filesWaiting = EvidenceServiceTiming.Measure(EvidenceServiceStage.FilesGateWait);
                    lock (filesGate)
                    {
                        filesWaiting.Dispose();
                        QueueObservation?.Span(EvidenceQueueStage.FilesGateWait, waiting, EvidenceStageMetrics.Now);
                        long aged = QueueObservation == null ? 0 : EvidenceStageMetrics.Now;
                        try { foreach (var pair in sources.ToArray()) if (pair.Value.BlockAge >= 0.1)
                        {
                            try { pair.Value.FlushBlock(); }
                            catch (Exception error) { FailSource(pair.Key, error); }
                        } }
                        finally { QueueObservation?.Span(EvidenceQueueStage.AgedBlockFlush, aged, EvidenceStageMetrics.Now); }
                    }
                    if (Seconds >= nextFlush) { FlushSources(false); nextFlush = Seconds + 1; }
                    if (Seconds >= nextPrune)
                    {
                        long pruning = QueueObservation == null ? 0 : EvidenceStageMetrics.Now;
                        try { Prune(); } catch (Exception error) { LastFailure = error.Message; }
                        finally { QueueObservation?.Span(EvidenceQueueStage.Prune, pruning, EvidenceStageMetrics.Now); }
                        nextPrune = Seconds + 5;
                    }
                }
                finally { EvidenceWorkerProfiling.EndWork(); QueueObservation?.EndBackground(EvidenceStageMetrics.Now); }
            }
            QueueObservation?.BeginBackground(EvidenceServiceContext.Close, EvidenceStageMetrics.Now);
            try { FlushSources(true); }
            finally { QueueObservation?.EndBackground(EvidenceStageMetrics.Now); }
            }
            catch (Exception error) { LastFailure = error.GetType().Name + ": " + error.Message; RecordShutdownWorkerFailure(error); }
            finally
            {
                QueueObservation?.BeginBackground(EvidenceServiceContext.Close, EvidenceStageMetrics.Now);
                try
                {
                lock (filesGate)
                {
                    foreach (var source in sources.Values) source.Abort();
                    sources.Clear(); Memory.Release(catalogBytes); durableFiles.Clear(); catalogBytes = 0;
                }
                Memory.Release(CodecWorkspaceBytes); Volatile.Write(ref closed, true);
                EvidenceWorkerProfiling.Stop();
                }
                finally { QueueObservation?.EndBackground(EvidenceStageMetrics.Now); }
            }
        }

        private void FlushSources(bool complete)
        {
            long started = QueueObservation == null ? 0 : EvidenceStageMetrics.Now;
            var waiting = EvidenceServiceTiming.Measure(EvidenceServiceStage.FilesGateWait);
            lock (filesGate)
            {
                waiting.Dispose();
                QueueObservation?.Span(EvidenceQueueStage.FilesGateWait, started, EvidenceStageMetrics.Now);
                long flushing = QueueObservation == null ? 0 : EvidenceStageMetrics.Now;
                try { FlushSourcesCore(complete); }
                finally { QueueObservation?.Span(EvidenceQueueStage.MaintenanceFlush, flushing, EvidenceStageMetrics.Now); }
            }
        }
        private void FlushSourcesCore(bool complete)
        {
            using var coverage = EvidenceServiceTiming.Measure(EvidenceServiceStage.SourceCoverage);
            string[] keys; lock (gate) keys = health.Keys.ToArray();
            foreach (string key in keys)
            {
                try
                {
                    bool active = sources.TryGetValue(key, out var source);
                    source?.Flush(true);
                    if (source?.Path != null) IndexFile(source.Path);
                    EvidenceCoverage snapshot;
                    lock (gate)
                    {
                        var state = health[key]; if (source != null) state.written = source.LastSequence;
                        state.flushed = state.written; state.queuedBytes = pendingBytes;
                        PublishRecovery(state);
                        if (active || complete)
                        {
                            state.complete = complete && state.failure == null && state.dropped == 0;
                            state.tailUnknown = !complete;
                        }
                        state.coverageRevision++;
                        snapshot = EvidenceJson.Decode<EvidenceCoverage>(EvidenceJson.Encode(state));
                    }
                    string directory = Resolve(key); Directory.CreateDirectory(directory);
                    string coveragePath = Path.Combine(directory, "coverage.json");
                    EvidenceJson.AtomicWrite(coveragePath, EvidenceJson.Encode(snapshot)); IndexFile(coveragePath);
                    if (snapshot.failure == null && ulong.TryParse(snapshot.flushed, out ulong persisted) && persisted > 0)
                        Volatile.Write(ref persistedRecord, 1);
                }
                catch (Exception error)
                {
                    FailSource(key, error);
                    // A final flush can fail after the last successful coverage write. Persist the new gap
                    // independently so shutdown cannot leave an old, apparently clean watermark behind.
                    try
                    {
                        string json;
                        lock (gate) { health[key].queuedBytes = pendingBytes; health[key].coverageRevision++; json = EvidenceJson.Encode(health[key]); }
                        string directory = Resolve(key); Directory.CreateDirectory(directory);
                        string coveragePath = Path.Combine(directory, "coverage.json");
                        EvidenceJson.AtomicWrite(coveragePath, json); IndexFile(coveragePath);
                    }
                    catch (Exception metadataError) { LastFailure = metadataError.GetType().Name + ": " + metadataError.Message; }
                }
            }
        }
        private static void GapRange(EvidenceCoverage state, ulong first, ulong last, string reason)
        {
            if (first > last || state.gaps.Any(g => g.First <= first && g.Last >= last)) return;
            for (int i = state.gaps.Count - 1; i >= 0; i--)
            {
                var gap = state.gaps[i]; ulong from = gap.First, to = gap.Last;
                if (from > last || to < first) continue;
                first = Math.Min(first, from); last = Math.Max(last, to); state.gaps.RemoveAt(i);
            }
            if (state.gaps.Count < 128) state.gaps.Add(new EvidenceGap { first = first.ToString(), last = last.ToString(),
                reason = reason, count = (long)Math.Min((ulong)long.MaxValue, last - first + 1), conservative = true });
            else
            {
                var tail = state.gaps[127]; tail.first = Math.Min(first, tail.First).ToString();
                tail.last = Math.Max(last, tail.Last).ToString(); tail.reason = "CoalescedCaptureGap"; tail.conservative = true;
            }
            state.gaps.Sort((a, b) => a.Last.CompareTo(b.Last));
        }
        private void FailSource(string key, Exception error)
        {
            LastFailure = error.GetType().Name + ": " + error.Message;
            lock (gate)
            {
                var state = health[key]; state.complete = false;
                ulong first = ulong.Parse(state.flushed) + 1, last = ulong.Parse(state.produced);
                if (first > last) MarkTailFailure(state, first);
                else MarkFailureRange(state, first, last);
                state.failure = LastFailure;
                GapRange(state, first, last, "WriteFailure");
                state.written = state.flushed;
            }
            if (sources.TryGetValue(key, out var source)) { source.Abort(); sources.Remove(key); }
        }
        private static double Seconds => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

        // Worker-only replication I/O. Only flushed bytes are advertised, and copies preserve their original path.
        public EvidenceFile[] Catalog(string run = null, string capture = null, string after = null, int maximum = 512)
        {
            lock (filesGate)
            {
                return durableFiles.Where(p => (run == null || p.Key.StartsWith(run + "/", StringComparison.Ordinal)) &&
                    (capture == null || p.Key.Contains("/sources/" + capture + "/")) && (after == null || StringComparer.Ordinal.Compare(p.Key, after) > 0))
                    .Take(Math.Min(512, maximum)).Select(p => p.Value).ToArray();
            }
        }
        private void IndexFile(string path)
        {
            using var indexing = EvidenceServiceTiming.Measure(EvidenceServiceStage.SourceIndex);
            string relative = path.Substring(Root.Length + 1).Replace('\\', '/');
            if (!relative.Contains("/sources/") || path.EndsWith(".tmp") || path.EndsWith(".partial") || path.EndsWith(".local.json")) return;
            if (!durableFiles.ContainsKey(relative))
            {
                long bytes = 768 + relative.Length * 4L;
                if (durableFiles.Count >= 65536 || !Memory.TryReserve(bytes)) { LastFailure = "DiagnosticCatalogBudgetExceeded"; return; }
                catalogBytes += bytes;
            }
            var info = new FileInfo(path);
            durableFiles[relative] = new EvidenceFile { path = relative, length = info.Length, revision = info.LastWriteTimeUtc.Ticks.ToString() };
        }
        public byte[] ReadBlock(string relative, long offset, int count)
        {
            if (count < 0 || count > MaximumBlockBytes || offset < 0) throw new InvalidDataException("Invalid block range.");
            using var stream = new FileStream(Resolve(relative), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Position = offset; byte[] result = new byte[Math.Min(count, (int)Math.Min(int.MaxValue, Math.Max(0, stream.Length - offset)))];
            int read = 0; while (read < result.Length) { int n = stream.Read(result, read, result.Length - read); if (n == 0) break; read += n; }
            if (read != result.Length) Array.Resize(ref result, read); return result;
        }
        public bool WasPruned(string relative)
        {
            string path = Resolve(relative); string name = Path.GetFileName(path);
            if (!name.StartsWith("events-", StringComparison.Ordinal) || !name.EndsWith(".jsonl", StringComparison.Ordinal)) return false;
            if (!ulong.TryParse(name.Substring(7, name.Length - 13), out ulong sequence)) return false;
            foreach (string metadata in new[] { "retention.json", "retention.local.json" })
            {
                string retention = Path.Combine(Path.GetDirectoryName(path), metadata);
                if (!File.Exists(retention)) continue;
                var value = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(retention));
                if (ulong.TryParse((string)value["beforeSequence"], out ulong boundary) && sequence < boundary) return true;
            }
            return false;
        }
        public long StoredLength(string relative)
        { string path = Resolve(relative); if (File.Exists(path + ".partial")) path += ".partial"; return File.Exists(path) ? new FileInfo(path).Length : 0; }
        public void CompleteImport(string relative, long expectedLength)
        {
            lock (filesGate)
            {
            string final = Resolve(relative), partial = final + ".partial";
            if (!File.Exists(partial) || new FileInfo(partial).Length != expectedLength) return;
            if (File.Exists(final)) File.Replace(partial, final, null); else File.Move(partial, final);
            IndexFile(final);
            }
        }
        public long ImportBlock(string relative, long offset, byte[] bytes, string hash, bool replace, long expectedLength = -1)
        { lock (filesGate) return ImportBlockCore(relative, offset, bytes, hash, replace, expectedLength); }
        private long ImportBlockCore(string relative, long offset, byte[] bytes, string hash, bool replace, long expectedLength)
        {
            if (bytes.Length > MaximumBlockBytes || EvidenceJson.Hash(bytes) != hash) throw new InvalidDataException("Evidence checksum mismatch.");
            string path = Resolve(relative);
            if (sources.Values.Any(s => path.StartsWith(s.Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Cannot overwrite locally originated evidence.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Reserve(Path.GetDirectoryName(path), bytes.Length + 4096);
            if (replace)
            {
                if (offset != 0 || !relative.EndsWith(".json")) throw new InvalidDataException("Invalid metadata replacement.");
                EvidenceJson.AtomicWrite(path, Encoding.UTF8.GetString(bytes)); IndexFile(path); return bytes.Length;
            }
            string finalPath = path;
            if (expectedLength >= 0)
            {
                path += ".partial";
                if (!File.Exists(path) && File.Exists(finalPath)) { Reserve(Path.GetDirectoryName(path), new FileInfo(finalPath).Length); File.Copy(finalPath, path); }
            }
            using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            if (offset < stream.Length)
            {
                stream.Position = offset; int overlap = (int)Math.Min(bytes.Length, stream.Length - offset);
                for (int i = 0; i < overlap; i++) if (stream.ReadByte() != bytes[i]) throw new InvalidDataException("ConflictingImportedBytes");
                return stream.Length; // Lost acknowledgements do not create another execution record.
            }
            if (offset != stream.Length) return stream.Length;
            stream.Position = offset;
            stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
            long length = stream.Length;
            if (expectedLength >= 0 && length == expectedLength)
            { stream.Dispose(); if (File.Exists(finalPath)) File.Replace(path, finalPath, null); else File.Move(path, finalPath); IndexFile(finalPath); }
            else if (expectedLength < 0) IndexFile(path);
            return length;
        }
        public void SaveReplication(object state) => EvidenceJson.AtomicWrite(Path.Combine(Root, "replication.json"), EvidenceJson.Encode(state));

        private void RecoverExisting()
        {
            foreach (string directory in Directory.EnumerateDirectories(Root, "sources", SearchOption.AllDirectories))
                foreach (string source in Directory.EnumerateDirectories(directory)) Recover(source);
            foreach (string path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) IndexFile(path);
        }
        private static void Recover(string directory)
        {
            using var recovering = EvidenceServiceTiming.Measure(EvidenceServiceStage.SourceRecover);
            foreach (string path in Directory.EnumerateFiles(directory, "events-*.jsonl"))
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                if (stream.Length == 0) continue;
                stream.Position = stream.Length - 1; if (stream.ReadByte() == 10) continue;
                long end = stream.Length - 1;
                while (end >= 0) { stream.Position = end; if (stream.ReadByte() == 10) break; end--; }
                // Preserve the exact interrupted bytes before repairing the appendable prefix.
                // Writing the marker first also keeps a crash during recovery conservative.
                string tail = Path.GetFileName(path) + ".interrupted-" + Guid.NewGuid().ToString("N") + ".tail";
                using (var original = new FileStream(Path.Combine(directory, tail), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                { stream.Position = end + 1; stream.CopyTo(original); original.Flush(true); }
                EvidenceJson.AtomicWrite(Path.Combine(directory, "recovery.json"), EvidenceJson.Encode(new {
                    tailUnknown = true, file = Path.GetFileName(path), retainedBytes = end + 1, originalTail = tail }));
                stream.SetLength(end + 1);
                stream.Flush(true);
            }
        }
        private sealed class EvidenceCapacityException : IOException { public EvidenceCapacityException() : base("Evidence capacity reached; replay interval is incomplete.") { } }
        private string RunDirectory(string path)
        {
            string relative = Path.GetFullPath(path).Substring(Root.Length + 1);
            return Resolve(relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0]);
        }
        private void Reserve(string path, long bytes)
        {
            using var reserving = EvidenceServiceTiming.Measure(EvidenceServiceStage.StorageReserve);
            string run = RunDirectory(path);
            if (!usage.TryGetValue(run, out long used)) usage[run] = used = Size(run);
            if (totalUsage < 0) totalUsage = Size(Root);
            long reserve = Math.Min(2 << 20, options.SessionBytes / 16);
            if (used + bytes + reserve > options.SessionBytes || totalUsage + bytes + reserve > options.TotalBytes)
            {
                Prune(bytes + reserve); used = Size(run); usage[run] = used; totalUsage = Size(Root);
                if (used + bytes + reserve > options.SessionBytes || totalUsage + bytes + reserve > options.TotalBytes) throw new EvidenceCapacityException();
            }
            usage[run] = used + bytes; totalUsage += bytes;
        }
        private void Prune(long needed = 0)
        { using var pruning = EvidenceServiceTiming.Measure(EvidenceServiceStage.Prune); lock (filesGate) PruneCore(needed); }
        private void PruneCore(long needed)
        {
            if (!Directory.Exists(Root)) return;
            long headroom = Math.Min(2 << 20, options.SessionBytes / 16);
            if (needed == 0 && totalUsage >= 0 && totalUsage + headroom < options.TotalBytes &&
                usage.Values.All(bytes => bytes + headroom < options.SessionBytes)) return;
            foreach (var source in sources.Values) source.Writer?.Flush();
            var runs = Directory.GetDirectories(Root).Where(d => Directory.EnumerateDirectories(d, "sources", SearchOption.AllDirectories).Any()).ToArray();
            foreach (string run in runs)
            {
                if (Size(run) + needed <= options.SessionBytes) continue;
                foreach (string parent in Directory.EnumerateDirectories(run, "sources", SearchOption.AllDirectories))
                    foreach (string source in Directory.GetDirectories(parent))
                    {
                        var files = Directory.GetFiles(source, "events-*.jsonl").OrderBy(p => p, StringComparer.Ordinal).ToArray();
                        int boundary = -1;
                        for (int i = 0; i < files.Length; i++)
                            if (IsRetainableCheckpoint(files[i], source)) boundary = i;
                        if (boundary <= 0) continue;
                        var removed = files.Take(boundary).Where(file => !sources.Values.Any(s => s.Stream != null && s.Path == file)).ToArray();
                        // Content-addressed inputs are retained only while some retained record references them.
                        var referenced = new HashSet<string>(StringComparer.Ordinal);
                        foreach (string file in files.Skip(boundary))
                            foreach (string line in ReadLinesShared(file))
                            {
                                try
                                {
                                    foreach (var record in EvidenceBlocks.Decode(line))
                                    {
                                    if (record.inputRef != null) referenced.Add(record.inputRef.Replace('\\', '/'));
                                    if (record.checkpointRef != null) referenced.Add(record.checkpointRef.Replace('\\', '/'));
                                    AddInlineReferences(record.input, referenced);
                                    }
                                }
                                catch { referenced.Add("unreadable-retained-record"); }
                            }
                        foreach (var active in sources.Values.Where(s => s.Directory == source)) referenced.UnionWith(active.HeldReferences);
                        try { ExpandReferences(source, referenced); }
                        catch (Exception error) { LastFailure = "RetentionDeferred:" + error.Message; continue; }
                        string sourceKey = source.Substring(Root.Length + 1).Replace('\\', '/');
                        bool local; lock (gate) local = health.ContainsKey(sourceKey);
                        if (removed.Length > 0)
                        {
                            EvidenceJson.AtomicWrite(Path.Combine(source, local ? "retention.json" : "retention.local.json"), EvidenceJson.Encode(new {
                                reason = "CapacityRetention", firstRetainedFile = Path.GetFileName(files[boundary]),
                                beforeSequence = Path.GetFileNameWithoutExtension(files[boundary]).Substring(7),
                                interpretation = "All earlier records and unreferenced blobs may have been removed; do not infer non-execution." }));
                            if (local) RecordLocalRetention("CapacityRetention:" + sourceKey);
                        }
                        foreach (string file in removed) File.Delete(file);
                        foreach (string folder in new[] { "inputs", "checkpoints" })
                            if (Directory.Exists(Path.Combine(source, folder)))
                                foreach (string blob in Directory.GetFiles(Path.Combine(source, folder), "*.json.gz"))
                                    if (!referenced.Contains(folder + "/" + Path.GetFileName(blob))) File.Delete(blob);
                    }
            }
            long total = Size(Root);
            foreach (string run in runs.OrderBy(Directory.GetLastWriteTimeUtc))
            {
                if (total + needed <= options.TotalBytes) break;
                if (sources.Values.Any(s => s.Directory.StartsWith(run + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
                long bytes = Size(run);
                if (!Path.GetFullPath(run).StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                string id = Path.GetFileName(run);
                // Recursive deletion can fail after removing some files. Mark locally produced
                // contexts before starting it, while ownership still exists in health.
                lock (gate)
                    if (health.Keys.Any(key => key.StartsWith(id + "/", StringComparison.Ordinal)))
                        RecordLocalRetention("GlobalCapacityRetention:" + id);
                Directory.Delete(run, true); total -= bytes;
                lock (gate)
                    foreach (string key in health.Keys.Where(k => k.StartsWith(id + "/", StringComparison.Ordinal)).ToArray()) health.Remove(key);
                File.AppendAllText(Path.Combine(Root, "retention.jsonl"), EvidenceJson.Encode(new { runId = id, reason = "GlobalCapacityRetention", utc = DateTime.UtcNow.ToString("o") }) + "\n");
            }
            usage.Clear(); totalUsage = Size(Root);
            foreach (string key in durableFiles.Keys.Where(k => !File.Exists(Resolve(k))).ToArray())
            { long bytes = 768 + key.Length * 4L; durableFiles.Remove(key); catalogBytes -= bytes; Memory.Release(bytes); }
        }
        private static bool IsRetainableCheckpoint(string file, string source)
        {
            string line = ReadLinesShared(file).FirstOrDefault();
            if (line == null || !line.Contains("\"stage\":\"replay.checkpoint\"")) return false;
            try
            {
                var record = EvidenceJson.Decode<DiagnosticRecord>(line);
                if (record.checkpointRef == null) return false;
                string blob = Path.GetFullPath(Path.Combine(source, record.checkpointRef));
                if (!blob.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(blob)) return false;
                byte[] bytes = EvidenceJson.Decompress(File.ReadAllBytes(blob), 16 << 20);
                if (Path.GetFileName(blob) != EvidenceJson.Hash(bytes) + ".json.gz") return false;
                record.input = EvidenceJson.Decode<ReplayCheckpointSet>(Encoding.UTF8.GetString(bytes));
                if (!IsRecoveryCheckpoint(record)) return false;
                ExpandReferences(source, new HashSet<string>(StringComparer.Ordinal) { record.checkpointRef });
                return true;
            }
            catch { return false; }
        }
        private static IEnumerable<string> ReadLinesShared(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            string line; while ((line = reader.ReadLine()) != null) yield return line;
        }
        private static long Size(string directory) => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
        public void RequestClose() { lock (gate) stopping = true; wake.Set(); }
        public void Dispose() { RequestClose(); worker.Join(100); }
        public bool WaitForClose(int milliseconds = 5000) => worker.Join(milliseconds);

        private sealed class Source : IDisposable
        {
            public readonly string Directory;
            public readonly HashSet<string> HeldReferences = new(StringComparer.Ordinal);
            public Stream Stream; public StreamWriter Writer;
            public string Path, LastSequence = "0";
            public long Bytes; public double Opened;
            private readonly IEvidenceStorage storage;
            private readonly Action<int> reserve;
            private readonly EvidenceStageMetrics metrics;
            private readonly EvidenceQueueObservation observation;
            private readonly StringBuilder block = new();
            private DiagnosticRecord pending;
            private string first, last;
            private int blockBytes, count;
            private double blockStarted;
            public double BlockAge => blockBytes == 0 && pending == null ? 0 : Seconds - blockStarted;
            public Source(string directory, IEvidenceStorage storage, Action<int> reserve, EvidenceStageMetrics metrics, EvidenceQueueObservation observation) { Directory = directory; this.storage = storage; this.reserve = reserve; this.metrics = metrics; this.observation = observation; }
            public void Rotate(double now, string sequence)
            {
                Dispose(); string name = "events-" + ulong.Parse(sequence).ToString("D20") + ".jsonl";
                Path = System.IO.Path.Combine(Directory, name);
                using (EvidenceServiceTiming.Measure(EvidenceServiceStage.EventOpen)) Stream = storage.OpenAppend(Path);
                Writer = new StreamWriter(Stream, new UTF8Encoding(false), 16 * 1024, true);
                Writer.NewLine = "\n";
                Bytes = Stream.Length; Opened = now;
            }
            public void Append(DiagnosticRecord record, double now)
            {
                using var appending = EvidenceServiceTiming.Measure(EvidenceServiceStage.EventAppend);
                if (blockBytes == 0 && pending == null) blockStarted = now;
                if (record.schemaVersion == 1 || record.stage == "replay.checkpoint" || record.stage == "replay.engine_checkpoint")
                {
                    FlushBlock();
                    string line;
                    using (EvidenceServiceTiming.Measure(EvidenceServiceStage.RecordJson)) line = EvidenceJson.EncodeBounded(record);
                    WriteLine(line); LastSequence = record.recordSequence;
                    if (observation != null && ulong.TryParse(record.recordSequence, out ulong sequence))
                        EvidenceServiceTiming.RecordFlushedBlockFromDirectory(Directory, sequence, sequence, 1, EvidenceServiceBlockKind.JsonLine);
                    return;
                }
                if (pending != null)
                {
                    if (CanFold(pending, record))
                    {
                        var call = pending; pending = null; call.stage = "replay.call";
                        call.completion = new DiagnosticCompletion { sequence = record.recordSequence, utc = record.utc,
                            monotonic = record.monotonicTime, network = record.networkTime, frame = record.frame, fixedStep = record.fixedStep, estimatedBytes = record.estimatedBytes };
                        Add(call, 2, record.recordSequence); return;
                    }
                    var previous = pending; pending = null; Add(previous, 1, previous.recordSequence);
                }
                if (record.stage == "replay.input") pending = record;
                else Add(record, 1, record.recordSequence);
            }
            public void AppendAdvances(AdvanceBlock value)
            {
                FlushBlock();
                WriteLine(EvidenceBlocks.EncodeAdvances(value.capture, value.run, value.round, value.entries, value.count, metrics));
                LastSequence = value.entries[value.count - 1].sequence.ToString();
                EvidenceServiceTiming.RecordFlushedBlockFromDirectory(Directory, value.entries[0].sequence,
                    value.entries[value.count - 1].sequence, value.count, EvidenceServiceBlockKind.Advance);
            }
            private static bool CanFold(DiagnosticRecord begin, DiagnosticRecord end) =>
                end.stage == "replay.output" && end.outcome == "Completed" && end.after == null && end.before == null && end.input == null && end.reason == null &&
                begin.engine == end.engine && begin.operation == end.operation && begin.role == end.role && begin.source == end.source && begin.target == end.target &&
                begin.eventId == null && end.eventId == null && begin.inputRef == null && end.inputRef == null &&
                begin.reason == null && begin.outcome == null && begin.after == null && begin.checkpointRef == null && end.checkpointRef == null &&
                begin.rootEventId == end.rootEventId && begin.parentEventId == end.parentEventId && begin.entityGeneration == end.entityGeneration &&
                begin.connectionEpoch == end.connectionEpoch && begin.assignmentEpoch == end.assignmentEpoch && begin.batchSequence == end.batchSequence &&
                begin.serverSequence == end.serverSequence && begin.stateVersion == end.stateVersion && begin.applicationRevision == end.applicationRevision &&
                begin.statusInstanceId == end.statusInstanceId && begin.tickIndex == end.tickIndex && !end.critical &&
                ulong.Parse(end.recordSequence) == ulong.Parse(begin.recordSequence) + 1;
            private void Add(DiagnosticRecord record, int records, string end)
            {
                long started = EvidenceStageMetrics.Now;
                string line;
                try { using (EvidenceServiceTiming.Measure(EvidenceServiceStage.RecordJson)) line = EvidenceJson.EncodeBounded(record, EvidenceBlocks.MaximumDecodedBytes - 4096); } finally { metrics.Encoding(started); }
                int bytes = Encoding.UTF8.GetByteCount(line) + 1;
                if (blockBytes != 0 && blockBytes + bytes > EvidenceBlocks.TargetBytes) FlushBlock();
                if (blockBytes == 0) { first = record.recordSequence; blockStarted = Seconds; }
                using (EvidenceServiceTiming.Measure(EvidenceServiceStage.BlockBuild))
                { block.Append(line).Append('\n'); blockBytes += bytes; last = end; count += records; }
                if (blockBytes >= EvidenceBlocks.TargetBytes) FlushBlock();
            }
            public void FlushBlock()
            {
                if (pending != null) { var value = pending; pending = null; Add(value, 1, value.recordSequence); }
                if (blockBytes == 0) return;
                byte[] raw;
                using (EvidenceServiceTiming.Measure(EvidenceServiceStage.BlockBuild)) raw = Encoding.UTF8.GetBytes(block.ToString());
                WriteLine(EvidenceBlocks.Encode(raw, first, last, count, metrics));
                if (observation != null && ulong.TryParse(first, out ulong firstSequence) && ulong.TryParse(last, out ulong lastSequence))
                    EvidenceServiceTiming.RecordFlushedBlockFromDirectory(Directory, firstSequence, lastSequence, count, EvidenceServiceBlockKind.JsonLine);
                LastSequence = last; block.Clear(); blockBytes = 0; count = 0;
            }
            private void WriteLine(string line)
            {
                int bytes = Encoding.UTF8.GetByteCount(line) + 1; long started = EvidenceStageMetrics.Now;
                try
                {
                    reserve(bytes);
                    long writing = observation == null ? 0 : EvidenceStageMetrics.Now;
                    try { using (EvidenceServiceTiming.Measure(EvidenceServiceStage.StreamWrite)) { Writer.WriteLine(line); Bytes += bytes; } }
                    finally { observation?.Span(EvidenceQueueStage.Write, writing, EvidenceStageMetrics.Now); }
                }
                finally { metrics.Storage(started); }
            }
            public void Flush(bool durable)
            {
                if (Writer == null) return;
                FlushBlock(); long started = EvidenceStageMetrics.Now;
                try
                {
                    try { using (EvidenceServiceTiming.Measure(EvidenceServiceStage.StreamFlush)) Writer.Flush(); }
                    finally { observation?.Span(EvidenceQueueStage.StreamFlush, started, EvidenceStageMetrics.Now); }
                    long flushing = observation == null ? 0 : EvidenceStageMetrics.Now;
                    try { using (EvidenceServiceTiming.Measure(EvidenceServiceStage.DurableFlush)) storage.Flush(Stream, durable); }
                    finally { observation?.Span(EvidenceQueueStage.DurableFlush, flushing, EvidenceStageMetrics.Now); }
                    HeldReferences.Clear();
                }
                finally { metrics.Storage(started); }
            }
            public void Abort()
            {
                block.Clear(); blockBytes = 0; count = 0; pending = null;
                try { Stream?.Dispose(); } catch { }
                try { Writer?.Dispose(); } catch { }
                Writer = null; Stream = null;
            }
            public void Dispose() { if (Writer != null) { try { Flush(true); } finally { Abort(); } } }
        }
    }
}
