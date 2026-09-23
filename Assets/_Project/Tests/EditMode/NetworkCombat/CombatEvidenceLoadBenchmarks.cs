using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    /// <summary>Real wall-clock paced CPU/storage probe. It does not measure Unity rendering, physics, or Steam.</summary>
    public sealed class CombatEvidenceLoadBenchmarks
    {
        private const int Frequency = 144;
        private const string Run = "load";
        private static readonly string Capture = new string('a', 32), RemoteCapture = new string('b', 32);
        private static double Seconds(long ticks) => ticks / (double)Stopwatch.Frequency;
        private static double Option(string name, double fallback, double min, double max) =>
            double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? Math.Max(min, Math.Min(max, value)) : fallback;

        [TestCase(50, "off")] [TestCase(50, "local")] [TestCase(50, "replicated")]
        [TestCase(200, "off")] [TestCase(200, "local")] [TestCase(200, "replicated")]
        [TestCase(500, "off")] [TestCase(500, "local")] [TestCase(500, "replicated")]
        [Category("CombatEvidenceLoadBenchmark")]
        public void StatusDominatedLoad(int controllers, string mode)
        {
            if (Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_BENCHMARK") != "1")
                Assert.Ignore("Opt in with COMBAT_EVIDENCE_BENCHMARK=1; full default matrix is 81 minutes plus drain time.");
            double duration = Option("COMBAT_EVIDENCE_BENCHMARK_SECONDS", 180, .25, 3600);
            int repeats = (int)Option("COMBAT_EVIDENCE_BENCHMARK_REPEATS", 3, 1, 20);
            double catchup = Option("COMBAT_EVIDENCE_BENCHMARK_CATCHUP_SECONDS", 45, 0, 600);
            string root = Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_BENCHMARK_OUTPUT") ??
                Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../Logs/CombatEvidenceLoadBenchmark"));
            var saved = CombatEvidence.Sink;
            try
            {
                CombatEvidence.Sink = null;
                int frames = (int)Math.Ceiling(duration * Frequency);
                // Recompute with the actual business implementations, without capture and without wall-clock waiting.
                var expected = new Workload(controllers, duration);
                for (int frame = 0; frame < frames; frame++) expected.Step(frame);
                string expectedHash = expected.Digest();
                var failures = new List<string>();
                for (int repeat = 1; repeat <= repeats; repeat++)
                {
                    string directory = Path.Combine(root, controllers + "-" + mode[0] + "-" + repeat + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    Directory.CreateDirectory(directory);
                    try { RunOne(directory, controllers, mode, duration, frames, catchup, expectedHash, repeat); }
                    catch (AssertionException error) { failures.Add("Repeat " + repeat + ": " + error.Message); }
                }
                Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
            }
            finally { CombatEvidence.Sink = saved; }
        }

        private static void RunOne(string directory, int count, string mode, double targetSeconds, int frames, double catchupSeconds, string expectedHash, int repeat)
        {
            var stores = new List<CombatEvidenceStore>();
            var replicators = new List<DiagnosticReplicator>();
            var memory = new DiagnosticMemoryBudget();
            var hub = new Hub();
            ProbeSink sink = null;
            var frameMs = new double[frames];
            var captureMs = new double[frames];
            var checkpointMs = new double[frames];
            var localFrameMs = new double[frames];
            long allocations = 0, deadlineMisses = 0, maximumRetained = 0;
            bool allocationMeasurementAvailable = ProbeAllocationCounter(out string allocationMeasurementReason);
            int[] collections = new int[3];
            double elapsed = 0, catchupElapsed = 0;
            long drainTicks = 0, drainCoverageChecks = 0;
            double drainTickSpan = 0;
            bool matched = false, caughtUp = mode != "replicated";
            object missing = Array.Empty<object>();
            string actualHash = null, failure = null;
            long produced = 0;
            try
            {
                if (mode != "off")
                {
                    var store = new CombatEvidenceStore(Path.Combine(directory, "primary"), new EvidenceStoreOptions { Memory = memory });
                    stores.Add(store); sink = new ProbeSink(store);
                    CombatEvidence.Sink = sink;
                    if (mode == "replicated")
                    {
                        stores.Add(new CombatEvidenceStore(Path.Combine(directory, "remote"), new EvidenceStoreOptions { Memory = memory }));
                        replicators.Add(new DiagnosticReplicator(stores[0], new Endpoint(hub, 0), Capture));
                        replicators.Add(new DiagnosticReplicator(stores[1], new Endpoint(hub, 1), RemoteCapture));
                        stores[1].TryWrite(new DiagnosticRecord { captureId = RemoteCapture, runId = Run, round = 1, recordSequence = "1",
                            role = "Process", stage = "process.start", outcome = "Started", input = "Synthetic remote receiver", critical = true });
                    }
                }
                else CombatEvidence.Sink = null;
                var workload = new Workload(count, targetSeconds);
                var clock = Stopwatch.StartNew();
                long allocatedStart = allocationMeasurementAvailable ? GC.GetAllocatedBytesForCurrentThread() : 0;
                collections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
                for (int frame = 0; frame < frames; frame++)
                {
                    Pace(clock, frame / (double)Frequency);
                    if (sink != null) { sink.Frame = frame; sink.NetworkTime = frame / (double)Frequency; }
                    long started = Stopwatch.GetTimestamp(), previousCapture = sink?.CaptureTicks ?? 0;
                    workload.Step(frame);
                    localFrameMs[frame] = Seconds(Stopwatch.GetTimestamp() - started) * 1000;
                    if (sink != null && frame > 0 && frame % (10 * Frequency) == 0)
                    {
                        long checkpointStarted = Stopwatch.GetTimestamp(); sink.Checkpoint();
                        checkpointMs[frame] = Seconds(Stopwatch.GetTimestamp() - checkpointStarted) * 1000;
                    }
                    foreach (var replication in replicators) replication.Tick(clock.Elapsed.TotalSeconds, Run);
                    frameMs[frame] = Seconds(Stopwatch.GetTimestamp() - started) * 1000;
                    captureMs[frame] = Seconds((sink?.CaptureTicks ?? 0) - previousCapture) * 1000;
                    if (clock.Elapsed.TotalSeconds > (frame + 1d) / Frequency) deadlineMisses++;
                    maximumRetained = Math.Max(maximumRetained, GC.GetTotalMemory(false));
                }
                elapsed = clock.Elapsed.TotalSeconds;
                allocations = allocationMeasurementAvailable ? GC.GetAllocatedBytesForCurrentThread() - allocatedStart : 0;
                collections = new[] { GC.CollectionCount(0) - collections[0], GC.CollectionCount(1) - collections[1], GC.CollectionCount(2) - collections[2] };
                using (CombatEvidence.Suppress()) actualHash = workload.Digest();
                matched = actualHash == expectedHash;
                sink?.Checkpoint();
                produced = sink?.Sequence ?? 0;
                CombatEvidence.Sink = null;
                if (stores.Count > 0)
                {
                    var drain = Stopwatch.StartNew();
                    double firstTick = 0, nextCoverageCheck = 0;
                    string requestedLast = produced.ToString(CultureInfo.InvariantCulture);
                    caughtUp = false;
                    while (drain.Elapsed.TotalSeconds < catchupSeconds)
                    {
                        // Keep the runtime Update cadence: a 50 ms Tick interval
                        // artificially limits each stop-and-wait file transfer.
                        Pace(drain, Math.Min(catchupSeconds, drainTicks / (double)Frequency));
                        double tickTime = drain.Elapsed.TotalSeconds;
                        if (tickTime >= catchupSeconds) break;
                        if (drainTicks == 0) firstTick = tickTime;
                        drainTickSpan = tickTime - firstTick;
                        drainTicks++;
                        foreach (var replication in replicators) replication.Tick(clock.Elapsed.TotalSeconds, Run);
                        if (tickTime < nextCoverageCheck) continue;
                        nextCoverageCheck = tickTime + .05;
                        drainCoverageChecks++;
                        bool flushed = ReadCoverage(stores[0].Root, Capture)?.flushed == requestedLast;
                        if (mode == "replicated") flushed &= ReadCoverage(stores[1].Root, Capture)?.flushed == requestedLast;
                        missing = mode == "replicated" ? MissingFiles(stores[0], stores[1]) : Array.Empty<object>();
                        caughtUp = flushed && (mode != "replicated" || ((object[])missing).Length == 0) && drain.Elapsed.TotalSeconds <= catchupSeconds;
                        if (caughtUp) break;
                    }
                    catchupElapsed = drain.Elapsed.TotalSeconds;
                }
            }
            catch (Exception error) { failure = error.ToString(); }
            finally
            {
                CombatEvidence.Sink = null;
                foreach (var replication in replicators) replication.Dispose();
                foreach (var store in stores) { store.Dispose(); if (!store.WaitForClose(30000)) failure = (failure ?? "") + " WriterDrainTimeout"; }
            }
            var report = new {
                schemaVersion = 2, kind = "status-dominated-paced-cpu-probe", controllers = count, mode, repeat, targetSeconds, frames,
                endpointCount = stores.Count,
                targetHz = Frequency, elapsedSeconds = elapsed, achievedHz = elapsed > 0 ? frames / elapsed : 0, deadlineMisses,
                scope = "EditMode CPU/status/Gateway/Replica/storage probe; synthetic two-endpoint transport in one process; not Unity total frame/GPU/physics/Steam performance",
                mainFrameSamplesPath = "frames.csv",
                mainFrame = Distribution(frameMs), businessAndCapture = Distribution(localFrameMs), captureOnly = Distribution(captureMs),
                checkpoint = Distribution(checkpointMs.Where(v => v > 0).ToArray()),
                mainThreadAllocatedBytes = allocationMeasurementAvailable ? (long?)allocations : null,
                allocationMeasurementAvailable, allocationMeasurementReason,
                managedHeapPeakBytes = maximumRetained, gcCollections = collections, diagnosticBudgetPeakBytes = memory.Peak,
                perEndpointQueuePeakBytes = stores.Select(s => s.PeakPendingBytes).ToArray(),
                writerMilliseconds = stores.Select(s => s.WriteMilliseconds).ToArray(),
                replicationMainMilliseconds = replicators.Select(r => r.MainMilliseconds).ToArray(), replicationWorkerMilliseconds = replicators.Select(r => r.WorkerMilliseconds).ToArray(),
                replicationSentBytes = replicators.Select(r => r.SentBytes).ToArray(), replicationFailures = replicators.Select(r => r.LastFailure).ToArray(),
                producedRecords = produced, dropped = stores.Select(s => s.Dropped).ToArray(), writerFailures = stores.Select(s => s.LastFailure).ToArray(),
                criticalDropped = stores.Select(s => ReadCoverage(s.Root, Capture)?.criticalDropped ?? 0).ToArray(),
                observationDropped = stores.Select(s => ReadCoverage(s.Root, Capture)?.observationDropped ?? 0).ToArray(),
                evidenceStages = stores.Select(s => s.Metrics.Snapshot()).ToArray(),
                hashImplementation = EvidenceJson.HashImplementation,
                eventBytes = EventBytes(Path.Combine(directory, "primary"), Capture), allEndpointDiskBytes = DiskBytes(directory),
                businessMatches = matched, expectedHash, actualHash, catchupSeconds = catchupElapsed, catchupComplete = caughtUp, missing,
                drainCadence = new { targetHz = Frequency, tickCount = drainTicks, measuredTickSpanSeconds = drainTickSpan,
                    achievedHz = drainTicks > 1 && drainTickSpan > 0 ? (double?)((drainTicks - 1) / drainTickSpan) : null,
                    coverageChecks = drainCoverageChecks, coverageCheckIntervalSeconds = .05 },
                statusControllerBinding = Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_BENCHMARK_INDEPENDENT_ENGINES") == "1" ? "independent-cold-start-stress" : "shared-replica-root",
                replayEngineCount = sink?.EngineCount ?? 0,
                coverage = stores.Select(s => ReadCoverage(s.Root, Capture)).ToArray(), failure,
                limitation = "GC collection counts/heap are process-wide and include the synthetic peer; managed allocations are the measured driver thread only. Timing is not a GPU or game-frame claim."
            };
            string output = Path.Combine(directory, "benchmark.json"); File.WriteAllText(output, EvidenceJson.Encode(report), new UTF8Encoding(false));
            using (var samples = new StreamWriter(Path.Combine(directory, "frames.csv"), false, new UTF8Encoding(false)))
            {
                samples.WriteLine("frame,mainMs,businessAndCaptureMs,captureMs,checkpointMs");
                for (int frame = 0; frame < frames; frame++) samples.WriteLine(string.Join(",", frame.ToString(CultureInfo.InvariantCulture),
                    frameMs[frame].ToString("R", CultureInfo.InvariantCulture), localFrameMs[frame].ToString("R", CultureInfo.InvariantCulture),
                    captureMs[frame].ToString("R", CultureInfo.InvariantCulture), checkpointMs[frame].ToString("R", CultureInfo.InvariantCulture)));
            }
            TestContext.WriteLine(output);
            Assert.That(failure, Is.Null, output); Assert.That(matched, Is.True, output);
            Assert.That(memory.Peak, Is.LessThanOrEqualTo(128L << 20), output);
            Assert.That(stores.All(s => s.PeakPendingBytes <= 32L << 20), Is.True, output);
            Assert.That(stores.Sum(s => s.Dropped), Is.Zero, output);
            // A slow link is an explicit result, not a fabricated failure or a claim of full replication.
        }

        private static bool ProbeAllocationCounter(out string reason)
        {
            try
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                var probe = new byte[32 << 10]; probe[0] = 1; GC.KeepAlive(probe);
                long observed = GC.GetAllocatedBytesForCurrentThread() - before;
                reason = observed >= probe.Length ? "ProbeAllocationObserved" : "CounterDidNotObserveProbeAllocation";
                return observed >= probe.Length;
            }
            catch (Exception error) { reason = error.GetType().Name; return false; }
        }

        private static void Pace(Stopwatch clock, double due)
        {
            while (true)
            {
                double left = due - clock.Elapsed.TotalSeconds;
                if (left <= 0) return;
                if (left >= .002) Thread.Sleep(Math.Max(1, (int)(left * 1000) - 1));
                else Thread.SpinWait(64);
            }
        }
        private static object Distribution(double[] values)
        {
            if (values.Length == 0) return new { count = 0, meanMs = 0d, p95Ms = 0d, p99Ms = 0d, maxMs = 0d };
            var sorted = (double[])values.Clone(); Array.Sort(sorted);
            return new { count = values.Length, meanMs = values.Average(), p95Ms = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1],
                p99Ms = sorted[(int)Math.Ceiling(sorted.Length * .99) - 1], maxMs = sorted[sorted.Length - 1] };
        }
        private static EvidenceCoverage ReadCoverage(string root, string capture)
        {
            string path = Path.Combine(root, Run, "1", "sources", capture, "coverage.json");
            try { return File.Exists(path) ? EvidenceJson.Decode<EvidenceCoverage>(File.ReadAllText(path)) : null; }
            catch (IOException) { return null; }
        }
        private static object[] MissingFiles(CombatEvidenceStore source, CombatEvidenceStore receiver)
        {
            var missing = new List<object>();
            string cursor = null;
            while (true)
            {
                var page = source.Catalog(Run, Capture, cursor);
                if (page.Length == 0) break;
                foreach (var file in page)
                {
                    if (!file.path.EndsWith(".jsonl", StringComparison.Ordinal) && !file.path.EndsWith(".gz", StringComparison.Ordinal)) continue;
                    string path = receiver.Resolve(file.path);
                    long actual = File.Exists(path) ? new FileInfo(path).Length : 0;
                    if (actual < file.length) missing.Add(new { file.path, expectedBytes = file.length, persistedBytes = actual });
                }
                cursor = page[page.Length - 1].path;
            }
            return missing.ToArray();
        }
        private static long EventBytes(string root, string capture)
        {
            string source = Path.Combine(root, Run, "1", "sources", capture);
            return Directory.Exists(source) ? Directory.EnumerateFiles(source, "events-*.jsonl").Sum(p => new FileInfo(p).Length) : 0;
        }
        private static long DiskBytes(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);

        private sealed class ProbeSink : IDiagnosticSink, IDiagnosticAdvanceSink, IDiagnosticIntegritySink
        {
            private readonly CombatEvidenceStore store;
            private readonly Dictionary<object, (string id, string domain, Func<object, object> capture)> engines = new();
            public int Frame; public double NetworkTime; public long Sequence, CaptureTicks;
            public int EngineCount => engines.Count;
            public ProbeSink(CombatEvidenceStore store) { this.store = store; }
            public string RegisterEngine(object engine, string domain, Func<object, object> capture)
            {
                if (engines.TryGetValue(engine, out var known)) return known.id;
                string id = domain + "." + (engines.Count + 1); engines.Add(engine, (id, domain, capture));
                Checkpoint(engine, id, domain, capture); return id;
            }
            public bool TryWrite(DiagnosticRecord record) => Write(record, null);
            private bool Write(DiagnosticRecord record, Func<object> capture)
            {
                long start = Stopwatch.GetTimestamp();
                try
                {
                    record.captureId = Capture; record.runId = Run; record.round = 1; record.recordSequence = (++Sequence).ToString();
                    record.frame = Frame; record.fixedStep = Frame * 50 / Frequency; record.networkTime = NetworkTime;
                    record.monotonicTime = Seconds(start); record.utc = DateTime.UtcNow.ToString("o");
                    record.estimatedBytes = (int)Math.Min(int.MaxValue, Math.Max(record.estimatedBytes,
                        512L + CombatEvidenceRuntime.RetainedBytes(record.input) + CombatEvidenceRuntime.RetainedBytes(record.before) + CombatEvidenceRuntime.RetainedBytes(record.after)));
                    return store.TryWrite(record, capture == null, capture);
                }
                finally { CaptureTicks += Stopwatch.GetTimestamp() - start; }
            }
            public void ReportCaptureFailure(DiagnosticRecord record)
            {
                record.captureId = Capture; record.runId = Run; record.round = 1; record.recordSequence = (++Sequence).ToString();
                store.ReportCaptureFailure(record);
            }
            public bool TryWriteAdvance(string role, string engine, string operation, float delta, StatusReplayBoundary boundary, int phase)
            {
                long start = Stopwatch.GetTimestamp();
                try { return store.TryWriteAdvance(Capture, Run, 1, new DiagnosticAdvance { role = role, engine = engine, operation = operation,
                    delta = delta, boundary = boundary, phase = phase, sequence = (ulong)++Sequence, utcTicks = DateTime.UtcNow.Ticks,
                    monotonic = Seconds(start), network = NetworkTime, frame = Frame, fixedStep = Frame * 50 / Frequency }); }
                finally { CaptureTicks += Stopwatch.GetTimestamp() - start; }
            }
            public void Checkpoint()
            {
                Write(new DiagnosticRecord { role = "Process", stage = "replay.checkpoint", engine = "*", outcome = "Captured",
                    critical = true, estimatedBytes = 8 << 20 }, () => {
                    using var suppressed = CombatEvidence.Suppress();
                    var states = new ReplayCheckpoint[engines.Count]; int index = 0;
                    foreach (var entry in engines) states[index++] = new ReplayCheckpoint { engine = entry.Value.id,
                        domain = entry.Value.domain, state = entry.Value.capture(entry.Key) };
                    return new ReplayCheckpointSet { engines = states };
                });
            }
            private void Checkpoint(object engine, string id, string domain, Func<object, object> capture)
            {
                Write(new DiagnosticRecord { role = domain, engine = id, stage = "replay.engine_checkpoint",
                    outcome = "Captured", critical = true, estimatedBytes = 8 << 20 }, () => {
                        using var suppressed = CombatEvidence.Suppress();
                        return new ReplayCheckpoint { domain = domain, engine = id, state = capture(engine) };
                    });
            }
        }

        private sealed class Workload
        {
            private readonly StatusController[] controllers;
            private readonly long[] tickCounts, tickDamage;
            private readonly ulong[] tickOrder;
            private readonly ServerCombatGateway gateway = new();
            private readonly CanonicalWorldReplica replica = new();
            private uint attack;
            public Workload(int count, double duration)
            {
                controllers = new StatusController[count]; tickCounts = new long[count]; tickDamage = new long[count]; tickOrder = new ulong[count];
                bool sharedReplica = Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_BENCHMARK_INDEPENDENT_ENGINES") != "1";
                gateway.RegisterClientIdentity(1, 1, 1); gateway.RegisterClientIdentity(2, 2, 1);
                gateway.Ledger.RegisterSource(1, 1); gateway.Ledger.RegisterSource(2, 2);
                for (int i = 0; i < count; i++)
                {
                    int index = i;
                    controllers[i] = new StatusController(tick => { tickCounts[index]++; tickDamage[index] += tick.Damage.Value;
                        unchecked { tickOrder[index] = (tickOrder[index] * 1099511628211UL) ^ tick.Instance.InstanceId.Value ^ (uint)tick.TickIndex; } });
                    gateway.Ledger.RegisterEntity((uint)(100 + i), 100000, CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
                    if (sharedReplica) replica.RegisterStatusController((uint)(100 + i), controllers[i]);
                    if (i % 4 == 0)
                        for (int source = 1; source <= 2; source++)
                            controllers[i].Apply(new StatusApplication(new StatusDefinition(EnemyStatusID.Burn, StatusStackMode.Add, 20), 3 + source,
                                (int)Math.Ceiling(duration / .35) + 8, .35f, 1, instanceId: new StatusInstanceId((ulong)(1000 + i * 2 + source)),
                                sourcePlayerId: (uint)source, sourceEntityId: (uint)source, targetEntityId: (uint)(100 + i)));
                }
            }
            public void Step(int frame)
            {
                // Preserve each actual float step and cross-controller order. No accumulated-delta shortcut.
                for (int i = 0; i < controllers.Length; i++) controllers[i].Advance(Delta(frame, i));
                if (frame % Frequency != 0) return;
                uint player = (uint)(frame / Frequency % 2 + 1), sequence = ++attack;
                var result = new CombatResult { EventId = CombatEventId.Compose((ushort)player, 1, sequence).Value, Sequence = sequence,
                    SourcePlayerId = player, SourceEntityId = player, TargetEntityId = (uint)(100 + sequence % controllers.Length), Damage = player == 1 ? 7 : 11 };
                var batch = new CombatSubmissionBatch { BatchSequence = sequence, Results = new[] { result } };
                replica.Apply(gateway.ProcessBatch(player, batch, frame / (double)Frequency));
                if (sequence % 3 == 0) replica.Apply(gateway.ProcessBatch(player, batch, frame / (double)Frequency));
            }
            private static float Delta(int frame, int controller) => ((frame + controller) % 3) switch {
                0 => 1f / Frequency, 1 => .007f, _ => .006888889f };
            public string Digest() => EvidenceJson.Hash(Encoding.UTF8.GetBytes(EvidenceJson.Encode(new {
                statuses = controllers.Select(c => c.CaptureReplayState()).ToArray(), tickCounts, tickDamage, tickOrder,
                gateway = gateway.CaptureReplayState(), replica = replica.CaptureReplayState() })));
        }
        private sealed class Hub { public readonly Dictionary<int, Endpoint> peers = new(); }
        private sealed class Endpoint : IDiagnosticReplicationTransport
        {
            private readonly Hub hub; private readonly int id; private readonly int[] peers;
            public Endpoint(Hub hub, int id) { this.hub = hub; this.id = id; peers = new[] { 1 - id }; hub.peers.Add(id, this); }
            public int[] Peers => peers; public bool IsHost => id == 0;
            public event Action<int, ArraySegment<byte>> Received;
            public bool Send(int peer, byte[] packet)
            {
                if (!hub.peers.TryGetValue(peer, out var destination)) return false;
                destination.Received?.Invoke(id, new ArraySegment<byte>(packet)); return true;
            }
        }
    }
}
