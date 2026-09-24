#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using NUnit.Framework;
using Unity.Profiling;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using RawProbe = MonsterSupergroup.Gameplay.Tests.CombatRawAllocationMeasurementPlayModeTests;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class CombatWorkloadAllocationPlayModeTests
    {
        private const int Frequency = 144, MeasuredSteps = 720, StepsPerUnityFrame = 24, Repeats = 3;
        private const int MaximumFrames = 64, MaximumThreads = 5, MaximumRawThreads = 128, MaximumSamples = 1048576, MaximumGcSpans = 2048;
        private const int ProfilerBufferBytes = 1 << 30;
        private const double WorkloadDuration = 5, CatchupSeconds = 45;
        private const string Run = "load";
        private static readonly string Capture = new('a', 32), RemoteCapture = new('b', 32);
        [ThreadStatic] private static WorkerContext currentWorker;

        [UnityTest]
        [Timeout(14400000)]
        [Category("CombatEvidenceAllocationWorkload")]
        public IEnumerator ExistingBenchmarkWorkloadHasCalibratedMainThreadAllocationBytes()
        {
            if (!Application.isBatchMode || Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_RAW_WORKLOAD_PROBE") != "1")
                Assert.Ignore("Requires isolated batch-mode Unity and COMBAT_EVIDENCE_RAW_WORKLOAD_PROBE=1.");
            EvidenceServiceCpuCounter.Probe();
            Assert.That(ProfilerDriver.deepProfiling, Is.False, "Deep profiling must be disabled for the bounded allocation probe.");
            string root = Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_ALLOCATION_PROBE_OUTPUT") ??
                Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/CombatEvidenceNextValidation/allocation-workload"));
            Directory.CreateDirectory(root);
            Type benchmark = Assembly.Load("MonsterSupergroup.NetworkCombat.Tests.EditMode").GetType(
                "MonsterSupergroup.NetworkCombat.Tests.CombatEvidenceLoadBenchmarks", true);
            var reports = new List<CaseReport>(27);
            foreach (int count in new[] { 50, 200, 500 })
            foreach (string mode in new[] { "off", "local", "replicated" })
            for (int repeat = 1; repeat <= Repeats; repeat++)
            {
                var report = new CaseReport { controllers = count, mode = mode, repeat = repeat };
                report.directory = Path.Combine(root, count + "-" + mode + "-" + repeat + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(report.directory);
                var run = RunCase(benchmark, report);
                try
                {
                    while (true)
                    {
                        bool next;
                        try { next = run.MoveNext(); }
                        catch (Exception error) { report.failure = error.ToString(); break; }
                        if (!next) break;
                        yield return run.Current;
                    }
                }
                finally { (run as IDisposable)?.Dispose(); }
                // A diagnostic gap or business failure does not invalidate correctly calibrated allocation observations.
                File.WriteAllText(Path.Combine(report.directory, "allocation.json"), EvidenceJson.Encode(report));
                reports.Add(report);
                File.WriteAllText(Path.Combine(root, "allocation-workload.json"), EvidenceJson.Encode(new {
                    schemaVersion = 2, expectedCases = 27, cases = reports, cpuTimingAcceptance = false }));
                if (report.workerCloseFailed) Assert.Fail("Profiler workers did not stop; remaining cases were not run: " + report.directory);
            }
            var failures = new List<string>();
            foreach (var report in reports)
                if (report.failure != null || !report.businessMatches || report.diagnosticDropped != 0 ||
                    !report.catchupComplete || report.observationFailures.Count != 0 || !report.allocationMeasurementAvailable || !report.gcMeasurementAvailable ||
                    !report.initializationAllocationAvailable || !report.initializationClockCorrelationVerified)
                    failures.Add(report.directory + ": execution=" + report.failure + "; allocation=" + report.measurementReason +
                        "; business=" + report.businessMatches + "; dropped=" + report.diagnosticDropped + "; catchup=" + report.catchupComplete);
            Assert.That(reports.Count, Is.EqualTo(27));
            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        }

        [Test]
        [Category("CombatEvidenceAllocationRawAudit")]
        public void ArchivedRawThreadEnumerationReportsActualBounds()
        {
            string input = Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_RAW_ENUMERATION_INPUT");
            string output = Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_RAW_ENUMERATION_OUTPUT");
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
                Assert.Ignore("Requires an archived allocation.json and a new independent audit output path.");
            input = Path.GetFullPath(input); output = Path.GetFullPath(output);
            var source = EvidenceJson.Decode<CaseReport>(File.ReadAllText(input));
            string raw = Path.GetFullPath(source.rawProfilePath);
            Assert.That(File.Exists(output), Is.False, "Never overwrite a previous audit.");
            Assert.That(output.StartsWith(Path.GetDirectoryName(input) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                Is.False, "Keep the archived case directory read-only.");
            Assert.That(ProfilerDriver.enabled || Profiler.enabled, Is.False, "The archived-data audit requires capture to be stopped.");
            string Hash(string path)
            {
                using var stream = File.OpenRead(path);
                using var digest = System.Security.Cryptography.SHA256.Create();
                return BitConverter.ToString(digest.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
            string rawBefore = Hash(raw), reportBefore = Hash(input);
            ProfilerDriver.ClearAllFrames();
            bool loaded = ProfilerDriver.LoadProfile(raw, false);
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            bool bounded = loaded && first >= 0 && last >= first && last - first + 1 <= MaximumFrames;
            var frames = new List<object>(MaximumFrames);
            var views = new List<object>(MaximumFrames * (MaximumRawThreads + 1));
            using (var iterator = new ProfilerFrameDataIterator())
                if (bounded)
                    for (int frame = first; frame <= last; frame++)
                    {
                        frames.Add(new { frame, actualThreadCount = iterator.GetThreadCount(frame) });
                        // The existing reader's exact 128-entry bound plus one sentinel, without reading sample payloads.
                        for (int index = 0; index <= MaximumRawThreads; index++)
                        {
                            using var data = ProfilerDriver.GetRawFrameDataView(frame, index);
                            views.Add(new { frame, index, valid = data.valid, threadId = data.valid ? data.threadId.ToString() : null,
                                threadName = data.valid ? data.threadName : null, samples = data.valid ? data.sampleCount : -1 });
                        }
                    }
            string rawAfter = Hash(raw), reportAfter = Hash(input);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, EvidenceJson.Encode(new { schemaVersion = 1, input, raw, loaded, bounded, first, last,
                rawBefore, rawAfter, reportBefore, reportAfter, inputUnchanged = rawBefore == rawAfter && reportBefore == reportAfter,
                expectedThreads = source.threads, frames, views }));
            ProfilerDriver.ClearAllFrames();
            Assert.That(bounded, Is.True, "The archived raw file must load within the original 64-frame bound.");
            Assert.That(rawAfter, Is.EqualTo(rawBefore)); Assert.That(reportAfter, Is.EqualTo(reportBefore));
        }

        private static IEnumerator RunCase(Type benchmark, CaseReport report)
        {
            var priorSink = CombatEvidence.Sink;
            var stores = new List<CombatEvidenceStore>(2);
            var replicators = new List<DiagnosticReplicator>(2);
            bool priorEnabled = ProfilerDriver.enabled, priorRuntime = Profiler.enabled, priorEditor = ProfilerDriver.profileEditor;
            bool priorCpu = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU), priorMemory = ProfilerDriver.IsAreaEnabled(ProfilerArea.Memory);
            bool priorAllocationCallstacks = Profiler.enableAllocationCallstacks;
            var priorBuffer = Profiler.maxUsedMemory;
            var priorSamples = Profiler.maxNumberOfSamplesPerFrame;
            var priorStarted = EvidenceWorkerProfiling.Started;
            var priorStopped = EvidenceWorkerProfiling.Stopped;
            var priorWorkStarted = EvidenceWorkerProfiling.WorkStarted;
            var priorWorkFinished = EvidenceWorkerProfiling.WorkFinished;
            var probe = new Probe(report);
            bool captureStarted = false, captureRead = false;
            object sink = null;
            Action checkpoint = null;
            var memory = new DiagnosticMemoryBudget();
            long observationOrigin = Stopwatch.GetTimestamp();
            try
            {
                CombatEvidence.Sink = null;
                var expected = new BoundWorkload(benchmark, report.controllers);
                for (int frame = 0; frame < MeasuredSteps; frame++) expected.Step(frame);
                report.expectedHash = expected.Digest();
                var pace = (Action<Stopwatch, double>)Delegate.CreateDelegate(typeof(Action<Stopwatch, double>),
                    benchmark.GetMethod("Pace", BindingFlags.NonPublic | BindingFlags.Static));

                ProfilerDriver.enabled = false; Profiler.enabled = false;
                ProfilerDriver.ClearAllFrames();
                ProfilerDriver.profileEditor = false;
                Profiler.enableAllocationCallstacks = false;
                report.deepProfilingEnabled = ProfilerDriver.deepProfiling;
                report.allocationCallstacksEnabled = Profiler.enableAllocationCallstacks;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true); ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, true);
                Profiler.maxUsedMemory = ProfilerBufferBytes;
                Profiler.maxNumberOfSamplesPerFrame = MaximumSamples;
                EvidenceWorkerProfiling.Started = probe.WorkerStarted;
                EvidenceWorkerProfiling.Stopped = probe.WorkerStopped;
                EvidenceWorkerProfiling.WorkStarted = probe.WorkStarted;
                EvidenceWorkerProfiling.WorkFinished = probe.WorkFinished;
                Application.logMessageReceivedThreaded += probe.Log;
                Profiler.enabled = true; ProfilerDriver.enabled = true; captureStarted = true;
                yield return null;
                probe.CalibrateMain();
                report.startupCollections.Capture();
                report.startupBeforeTicks = Stopwatch.GetTimestamp();
                using (probe.startup.Auto()) { }
                report.startupAfterTicks = Stopwatch.GetTimestamp();
                if (report.mode != "off")
                {
                    stores.Add(new CombatEvidenceStore(Path.Combine(report.directory, "primary"), new EvidenceStoreOptions {
                        Memory = memory, ObserveQueue = true, ObservationOriginTicks = observationOrigin }));
                    sink = Activator.CreateInstance(Nested(benchmark, "ProbeSink"), stores[0]);
                    CombatEvidence.Sink = (IDiagnosticSink)sink;
                    if (report.mode == "replicated")
                    {
                        stores.Add(new CombatEvidenceStore(Path.Combine(report.directory, "remote"), new EvidenceStoreOptions {
                            Memory = memory, ObserveQueue = true, ObservationOriginTicks = observationOrigin }));
                        object hub = Activator.CreateInstance(Nested(benchmark, "Hub"), true);
                        Type endpoint = Nested(benchmark, "Endpoint");
                        replicators.Add(new DiagnosticReplicator(stores[0], (IDiagnosticReplicationTransport)Activator.CreateInstance(endpoint, hub, 0), Capture));
                        replicators.Add(new DiagnosticReplicator(stores[1], (IDiagnosticReplicationTransport)Activator.CreateInstance(endpoint, hub, 1), RemoteCapture));
                        stores[1].TryWrite(new DiagnosticRecord { captureId = RemoteCapture, runId = Run, round = 1, recordSequence = "1",
                            role = "Process", stage = "process.start", outcome = "Started", input = "Synthetic remote receiver", critical = true });
                    }
                }
                report.expectedThreads = report.mode == "off" ? 1 : report.mode == "local" ? 2 : 5;
                var ready = Stopwatch.StartNew();
                while (Volatile.Read(ref probe.calibratedWorkers) < report.expectedThreads - 1)
                {
                    if (ready.Elapsed.TotalSeconds > 10) throw new TimeoutException("Profiler worker calibration did not finish.");
                    probe.CheckFrames(); yield return null;
                }
                report.workloadInitCollections.Capture();
                report.workloadInitBeforeTicks = Stopwatch.GetTimestamp();
                using (probe.workloadInit.Auto()) { }
                report.workloadInitAfterTicks = Stopwatch.GetTimestamp();
                var workload = new BoundWorkload(benchmark, report.controllers);
                FieldInfo sinkFrame = sink?.GetType().GetField("Frame"), sinkTime = sink?.GetType().GetField("NetworkTime");
                var producedSequence = sink == null ? null : (Func<ulong>)Delegate.CreateDelegate(typeof(Func<ulong>), sink,
                    sink.GetType().GetMethod("ProducedSequence", BindingFlags.Instance | BindingFlags.Public));
                var mainObservation = stores.Count > 0 ? stores[0].QueueObservation : null;
                checkpoint = sink == null ? null : (Action)Delegate.CreateDelegate(typeof(Action), sink,
                    sink.GetType().GetMethod("Checkpoint", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null));
                long initialTicks = workload.TotalTicks();
                long initialDropped = Dropped(stores);
                report.loadCollections.Capture();
                int[] gcBefore = report.loadCollections.counts;
                var clock = Stopwatch.StartNew();
                foreach (var store in stores) store.QueueObservation?.MarkPhase(EvidenceQueuePhase.Load, Stopwatch.GetTimestamp());
                report.loadStartBeforeTicks = Stopwatch.GetTimestamp();
                using (probe.loadStart.Auto()) { }
                report.loadStartAfterTicks = Stopwatch.GetTimestamp();
                for (int frame = 0; frame < MeasuredSteps; frame++)
                {
                    // Match the normal benchmark's 0..719 inputs; reflection and pacing stay outside the work scope.
                    sinkFrame?.SetValue(sink, frame); sinkTime?.SetValue(sink, frame / (double)Frequency);
                    pace(clock, frame / (double)Frequency);
                    using (probe.step.Auto())
                    {
                        long started = Stopwatch.GetTimestamp();
                        mainObservation?.BeginMainFrame(frame, started, producedSequence?.Invoke() ?? 0);
                        try
                        {
                            using (DiagnosticMainTiming.Measure(DiagnosticMainStage.WorkloadStep)) workload.Step(frame);
                            using (DiagnosticMainTiming.Measure(DiagnosticMainStage.ReplicationTick))
                                foreach (var replication in replicators) replication.Tick(clock.Elapsed.TotalSeconds, Run);
                        }
                        finally { mainObservation?.EndMainFrame(Stopwatch.GetTimestamp(), producedSequence?.Invoke() ?? 0); }
                    }
                    report.executedSteps++;
                    if ((frame + 1) % StepsPerUnityFrame == 0) { probe.CheckFrames(); yield return null; }
                }
                report.loadEndBeforeTicks = Stopwatch.GetTimestamp();
                using (probe.loadEnd.Auto()) { }
                report.loadEndAfterTicks = Stopwatch.GetTimestamp();
                report.measuredWindowWallSeconds = clock.Elapsed.TotalSeconds;
                report.achievedHz = report.executedSteps / report.measuredWindowWallSeconds;
                report.loadEndCollections.Capture();
                report.gcCollectionsInLoad = report.gcCollectionCounterVerified ? Difference(report.loadEndCollections.counts, gcBefore) : null;
                report.measuredWindowDropped = Dropped(stores) - initialDropped;
                report.dotTicksInWindow = workload.TotalTicks() - initialTicks;
                foreach (var store in stores) store.QueueObservation?.MarkPhase(EvidenceQueuePhase.Catchup, Stopwatch.GetTimestamp());
                var drain = Stopwatch.StartNew();
                checkpoint?.Invoke();
                long produced = sink == null ? 0 : (long)sink.GetType().GetField("Sequence").GetValue(sink);
                report.producedRecords = produced;
                CombatEvidence.Sink = null;
                // Complete the sampled frame; this tail is saved but excluded by the load timestamp anchors.
                // Its time is included in the 45-second catch-up deadline, never silently omitted.
                for (int i = 0; i < 2; i++)
                {
                    foreach (var replication in replicators) replication.Tick(clock.Elapsed.TotalSeconds, Run);
                    probe.CheckFrames(); yield return null;
                }
                captureRead = true; probe.StopCapture();
                report.postLoadCaptureTailSeconds = drain.Elapsed.TotalSeconds;

                using (CombatEvidence.Suppress()) report.actualHash = workload.Digest();
                report.businessMatches = report.actualHash == report.expectedHash;
                report.catchupComplete = stores.Count == 0;
                long ticks = (long)Math.Ceiling(drain.Elapsed.TotalSeconds * Frequency);
                double nextCoverageCheck = 0;
                MethodInfo missingFiles = benchmark.GetMethod("MissingFiles", BindingFlags.Static | BindingFlags.NonPublic);
                while (stores.Count > 0 && drain.Elapsed.TotalSeconds < CatchupSeconds)
                {
                    pace(drain, Math.Min(CatchupSeconds, ticks++ / (double)Frequency));
                    if (drain.Elapsed.TotalSeconds >= CatchupSeconds) break;
                    foreach (var replication in replicators) replication.Tick(clock.Elapsed.TotalSeconds, Run);
                    if (drain.Elapsed.TotalSeconds < nextCoverageCheck) { if (ticks % StepsPerUnityFrame == 0) yield return null; continue; }
                    nextCoverageCheck = drain.Elapsed.TotalSeconds + .05;
                    string last = produced.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    bool flushed = PollCoverageFile(CoveragePath(stores[0].Root))?.flushed == last;
                    if (stores.Count == 2) flushed &= PollCoverageFile(CoveragePath(stores[1].Root))?.flushed == last;
                    var missing = stores.Count == 2 ? (object[])missingFiles.Invoke(null, new object[] { stores[0], stores[1] }) : Array.Empty<object>();
                    if (flushed && missing.Length == 0 && drain.Elapsed.TotalSeconds <= CatchupSeconds) { report.catchupComplete = true; break; }
                    if (ticks % StepsPerUnityFrame == 0) yield return null;
                }
                report.catchupWallSeconds = stores.Count == 0 ? 0 : drain.Elapsed.TotalSeconds;
                report.budgetReservationPeakBytes = memory.Peak;
            }
            finally
            {
                if (captureStarted && !captureRead)
                    try { probe.StopCapture(); } catch (Exception error) { report.measurementReason = error.ToString(); }
                ProfilerDriver.enabled = false; Profiler.enabled = false;
                CombatEvidence.Sink = null;
                foreach (var store in stores) store.QueueObservation?.MarkPhase(EvidenceQueuePhase.Close, Stopwatch.GetTimestamp());
                foreach (var replication in replicators)
                {
                    replication.Dispose();
                    if (!replication.WaitForClose(30000)) { report.workerCloseFailed = true; report.failure = "ReplicationWorkerDidNotClose"; }
                    if (replication.LastFailure != null) report.replicationFailures.Add(replication.LastFailure);
                }
                foreach (var store in stores)
                {
                    store.Dispose();
                    bool closed = store.WaitForClose(30000);
                    if (!closed) { report.workerCloseFailed = true; report.failure = "StorageWorkerDidNotClose"; }
                    report.diagnosticDropped += store.Dropped;
                    if (store.LastFailure != null) report.writerFailures.Add(store.LastFailure);
                    try { report.coverage.Add(Coverage(store.Root)); } catch (Exception error) { report.writerFailures.Add("Coverage:" + error.GetType().Name); }
                }
                foreach (var store in stores)
                {
                    string name = Path.GetFileName(store.Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    bool exported = false;
                    try { exported = !report.workerCloseFailed && store.ExportQueueObservation(Path.Combine(report.directory, name + "-queue-observation.json")); }
                    catch (Exception error) { report.observationFailures.Add(name + ":" + error.GetType().Name); }
                    if (!exported) report.observationFailures.Add(name + ":" + (store.ObservationUnavailableReason ?? "ExportUnavailable"));
                }
                // Saving/parsing large raw files must not give the writer unaccounted time before the catch-up timer.
                if (captureStarted)
                    try { probe.ReadCapture(); } catch (Exception error) { report.measurementReason = "RawRead:" + error; }
                report.profilingHookFailures = EvidenceWorkerProfiling.FailureCount - report.profilingHookFailuresBefore;
                if (report.profilingHookFailures != 0)
                {
                    report.allocationMeasurementAvailable = false; report.mainThreadAllocationAvailable = false;
                    report.gcMeasurementAvailable = false; report.scopedMainThreadAllocatedBytes = null;
                    report.observedGcCollectUnionMs = null; report.measurementReason = "ProfilerWorkerHookFailed:" + EvidenceWorkerProfiling.LastFailure;
                    probe.InvalidatePhases("ProfilerWorkerHookFailed");
                    if (report.threads != null) foreach (var thread in report.threads)
                        if (thread != null) { thread.allocationAvailable = false; thread.loadAllocationBytes = null; }
                }
                Application.logMessageReceivedThreaded -= probe.Log;
                probe.FinalizeProfilerErrors();
                EvidenceWorkerProfiling.Started = priorStarted; EvidenceWorkerProfiling.Stopped = priorStopped;
                EvidenceWorkerProfiling.WorkStarted = priorWorkStarted; EvidenceWorkerProfiling.WorkFinished = priorWorkFinished;
                Profiler.maxUsedMemory = priorBuffer; Profiler.maxNumberOfSamplesPerFrame = priorSamples;
                Profiler.enableAllocationCallstacks = priorAllocationCallstacks;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, priorCpu); ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, priorMemory);
                ProfilerDriver.profileEditor = priorEditor; Profiler.enabled = priorRuntime; ProfilerDriver.enabled = priorEnabled;
                CombatEvidence.Sink = priorSink;
            }
        }

        private sealed class Probe
        {
            private readonly CaseReport report;
            private readonly string prefix;
            public readonly ProfilerMarker step, startup, workloadInit, loadStart, loadEnd;
            private readonly ProfilerMarker gcCalibration;
            public int calibratedWorkers;
            private int workerCount, overflowLogs;
            private string firstOverflowLog;
            private volatile bool recording = true;
            private int frameOrigin = -1;
            private readonly ThreadReport[] threads = new ThreadReport[MaximumThreads];
            private readonly GcSpan[] gcSpans = new GcSpan[MaximumGcSpans];
            private int gcSpanCount;
            private ulong startupBegin, workloadInitBegin, begin, end;
            private ulong requiredLoadFrames;
            public Probe(CaseReport report)
            {
                this.report = report; prefix = "CombatEvidence.FullAllocation." + Guid.NewGuid().ToString("N");
                report.profilingHookFailuresBefore = EvidenceWorkerProfiling.FailureCount;
                step = new ProfilerMarker(prefix + ".Step"); loadStart = new ProfilerMarker(prefix + ".LoadStart");
                loadEnd = new ProfilerMarker(prefix + ".LoadEnd"); gcCalibration = new ProfilerMarker(prefix + ".GcCalibration");
                startup = new ProfilerMarker(prefix + ".Startup"); workloadInit = new ProfilerMarker(prefix + ".WorkloadInit");
                threads[0] = new ThreadReport(prefix, "main", "Main Thread");
            }
            public void CalibrateMain()
            {
                using (new ProfilerMarker(threads[0].calibration.marker).Auto())
                {
                    Calibrate(threads[0]);
                    report.gcCounterCalibrationBefore = Collections();
                    using (gcCalibration.Auto()) GC.Collect();
                    report.gcCounterCalibrationAfter = Collections();
                }
                report.gcCollectionCounterVerified = report.gcCounterCalibrationAfter[0] > report.gcCounterCalibrationBefore[0] &&
                    report.gcCounterCalibrationAfter[1] >= report.gcCounterCalibrationBefore[1] &&
                    report.gcCounterCalibrationAfter[2] >= report.gcCounterCalibrationBefore[2];
            }
            private static void Calibrate(ThreadReport thread)
            {
                RawProbe.Allocate(32768); RawProbe.Allocate(65536);
                using (new ProfilerMarker(thread.empty.marker).Auto()) RawProbe.Allocate(0);
                using (new ProfilerMarker(thread.small.marker).Auto()) RawProbe.Allocate(32768);
                using (new ProfilerMarker(thread.large.marker).Auto()) RawProbe.Allocate(65536);
            }
            public void WorkerStarted(string role, string identity)
            {
                int index = Interlocked.Increment(ref workerCount);
                if (index >= MaximumThreads) { Interlocked.Increment(ref overflowLogs); return; }
                string endpoint = Path.GetFileName(identity.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var thread = new ThreadReport(prefix, role + ":" + endpoint, prefix + "." + role + "." + endpoint);
                threads[index] = thread;
                Profiler.BeginThreadProfiling("CombatEvidenceAllocation", thread.threadName);
                try
                {
                    using (new ProfilerMarker(thread.calibration.marker).Auto())
                    {
                        currentWorker = new WorkerContext { owner = this, thread = thread };
                        Calibrate(thread);
                    }
                }
                finally { Interlocked.Increment(ref calibratedWorkers); }
            }
            public void WorkerStopped()
            {
                if (currentWorker?.owner != this) return;
                if (currentWorker.open) { Profiler.EndSample(); currentWorker.open = false; }
                Profiler.EndThreadProfiling(); currentWorker = null;
            }
            public void WorkStarted()
            {
                var worker = currentWorker;
                if (!recording || worker?.owner != this) return;
                if (worker.open) { Interlocked.Increment(ref overflowLogs); return; }
                Profiler.BeginSample(worker.thread.workMarker);
                worker.open = true;
            }
            public void WorkFinished()
            {
                var worker = currentWorker;
                if (worker?.owner != this || !worker.open) return;
                Profiler.EndSample(); worker.open = false;
            }
            public void CheckFrames()
            {
                int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
                if (first < 0) return;
                if (frameOrigin < 0) frameOrigin = first;
                if (first != frameOrigin || last - frameOrigin + 1 >= MaximumFrames)
                    throw new InvalidOperationException("RawProfilerFrameCapacityOrHistoryLoss");
            }
            public void Log(string condition, string stack, LogType type)
            {
                if (condition.IndexOf("profil", StringComparison.OrdinalIgnoreCase) < 0) return;
                if (condition.IndexOf("overflow", StringComparison.OrdinalIgnoreCase) < 0 &&
                    condition.IndexOf("dropped", StringComparison.OrdinalIgnoreCase) < 0 &&
                    condition.IndexOf("out of memory", StringComparison.OrdinalIgnoreCase) < 0 &&
                    condition.IndexOf("buffer", StringComparison.OrdinalIgnoreCase) < 0) return;
                Interlocked.Increment(ref overflowLogs);
                Interlocked.CompareExchange(ref firstOverflowLog, condition.Length > 2048 ? condition.Substring(0, 2048) : condition, null);
            }
            public void StopCapture()
            {
                recording = false; ProfilerDriver.enabled = false; Profiler.enabled = false;
                report.firstFrame = ProfilerDriver.firstFrameIndex; report.lastFrame = ProfilerDriver.lastFrameIndex;
                report.rawFrameCount = report.firstFrame < 0 ? 0 : report.lastFrame - report.firstFrame + 1;
            }
            public void ReadCapture()
            {
                report.rawProfilePath = Path.Combine(report.directory, "load.raw");
                report.profilerOverflowLogs = overflowLogs; report.firstProfilerOverflowLog = firstOverflowLog;
                report.profilingHookFailures = EvidenceWorkerProfiling.FailureCount - report.profilingHookFailuresBefore;
                bool bounded = report.rawFrameCount > 0 && report.rawFrameCount <= MaximumFrames &&
                    (frameOrigin < 0 || frameOrigin == report.firstFrame);
                // Preserve even incomplete raw history; boundedness remains a separate validity gate.
                if (report.rawFrameCount > 0)
                {
                    report.rawProfileSaved = ProfilerDriver.SaveProfile(report.rawProfilePath) &&
                        File.Exists(report.rawProfilePath) && new FileInfo(report.rawProfilePath).Length > 0;
                }
                if (bounded)
                {
                    FindAnchors();
                    using var iterator = new ProfilerFrameDataIterator();
                    int expectedTargets = 0;
                    foreach (var thread in threads) if (thread != null) expectedTargets++;
                    for (int frame = report.firstFrame; frame <= report.lastFrame; frame++)
                    {
                        int total = iterator.GetThreadCount(frame), scanned = 0, matched = 0, matchedMask = 0;
                        bool mainSeen = false, intersectsLoad = false;
                        report.totalRawThreadsPeak = Math.Max(report.totalRawThreadsPeak, total);
                        for (int index = 0; index < Math.Min(total, MaximumRawThreads); index++)
                        {
                            using var data = ProfilerDriver.GetRawFrameDataView(frame, index);
                            scanned++;
                            if (!data.valid) { report.invalidRawThreadView = true; break; }
                            ThreadReport target = null;
                            int targetIndex = -1;
                            for (int candidate = 0; candidate < threads.Length; candidate++)
                                if (threads[candidate] != null && threads[candidate].threadName == data.threadName)
                                { target = threads[candidate]; targetIndex = candidate; break; }
                            if (target == null) continue;
                            if ((matchedMask & (1 << targetIndex)) != 0)
                            {
                                report.duplicateTargetThreadView = true;
                                report.firstRawReadFailure ??= "DuplicateTargetThreadView: frame=" + frame + ", index=" + index + ", role=" + target.role;
                                continue;
                            }
                            matchedMask |= 1 << targetIndex;
                            matched++;
                            if (target.role == "main")
                            {
                                mainSeen = true;
                                intersectsLoad = data.frameStartTimeNs < end && data.frameStartTimeNs + data.frameTimeNs > begin;
                            }
                            ReadThread(data, target, frame, index);
                            // Only the registered workload threads are in scope. Unrelated Unity workers do not consume
                            // their sample capacity, and their presence beyond this fixed scan bound is not missing evidence.
                            if (matched == expectedTargets) break;
                        }
                        bool truncated = scanned == MaximumRawThreads && total > MaximumRawThreads && matched < expectedTargets;
                        bool complete = TargetThreadFrameComplete(mainSeen, intersectsLoad, matched, expectedTargets);
                        report.rawThreadScanTruncated |= truncated;
                        report.requiredTargetThreadFrameMissing |= !complete;
                        if (!complete) report.firstRawReadFailure ??= "RequiredTargetThreadFrameMissing: frame=" + frame +
                            ", matched=" + matched + ", expected=" + expectedTargets + ", scanned=" + scanned + ", total=" + total;
                        report.rawThreadScans.Add(new RawThreadScanReport { frame = frame, totalRawThreads = total,
                            scannedThreads = scanned, matchedTargets = matched, expectedTargets = expectedTargets,
                            mainSeen = mainSeen, intersectsLoad = intersectsLoad, scanTruncated = truncated, requiredTargetsComplete = complete });
                    }
                }
                report.threads = threads;
                bool calibrated = workerCount + 1 == report.expectedThreads;
                long scoped = 0;
                foreach (var thread in threads)
                {
                    if (thread == null) continue;
                    thread.calibrated = CalibrationPassed(thread.empty, thread.small, thread.large);
                    thread.missingLoadFrames = CountBits(requiredLoadFrames & ~thread.frameMask);
                    thread.allocationAvailable = thread.calibrated && thread.missingByteMetadata == 0 && thread.rawFrames > 0 &&
                        requiredLoadFrames != 0 && thread.missingLoadFrames == 0;
                    if (thread.allocationAvailable) thread.loadAllocationBytes = thread.observedLoadBytes;
                    calibrated &= thread.allocationAvailable;
                    if (thread.role == "main") scoped = thread.stepBytes;
                }
                double seconds = end > begin ? (end - begin) / 1e9 : 0;
                report.anchorWindowSeconds = seconds;
                report.rawStartupStartNs = startupBegin; report.rawWorkloadInitStartNs = workloadInitBegin;
                report.rawLoadStartNs = begin; report.rawLoadEndNs = end;
                double tickNs = 1e9 / Stopwatch.Frequency;
                double startOffset = begin - (report.loadStartBeforeTicks * .5 + report.loadStartAfterTicks * .5) * tickNs;
                double endOffset = end - (report.loadEndBeforeTicks * .5 + report.loadEndAfterTicks * .5) * tickNs;
                report.profilerMinusStopwatchStartNs = startOffset; report.profilerMinusStopwatchEndNs = endOffset;
                report.clockMappingErrorBoundMs = (Math.Abs(endOffset - startOffset) +
                    Math.Max(report.loadStartAfterTicks - report.loadStartBeforeTicks, report.loadEndAfterTicks - report.loadEndBeforeTicks) * tickNs / 2) / 1e6;
                bool anchors = report.loadStartOccurrences == 1 && report.loadEndOccurrences == 1 && end > begin &&
                    Math.Abs(seconds - report.measuredWindowWallSeconds) < .25;
                report.queueClockCorrelationVerified = anchors && report.clockMappingErrorBoundMs <= 5;
                bool completeMainHistory = report.rawFrameCount == 64 ? threads[0].frameMask == ulong.MaxValue :
                    threads[0].frameMask == ((1UL << report.rawFrameCount) - 1);
                bool rawValid = report.rawProfileSaved && bounded && overflowLogs == 0 && report.profilingHookFailures == 0 &&
                    !report.sampleCapacityReached && !report.threadIdentityChanged && !report.duplicateTargetThreadView &&
                    !report.requiredTargetThreadFrameMissing && anchors && completeMainHistory;
                report.allocationMeasurementAvailable = MeasurementValid(report.rawProfileSaved, bounded,
                    overflowLogs == 0 && report.profilingHookFailures == 0 && !report.sampleCapacityReached &&
                    !report.threadIdentityChanged && !report.duplicateTargetThreadView && !report.requiredTargetThreadFrameMissing && completeMainHistory,
                    anchors, calibrated, report.stepOccurrences, report.executedSteps);
                if (!rawValid)
                    foreach (var thread in threads) if (thread != null) { thread.allocationAvailable = false; thread.loadAllocationBytes = null; }
                report.measurementReason = report.allocationMeasurementAvailable ? "CalibratedRawMetadataAndComplete720StepWindow" :
                    "Unverified: saved=" + report.rawProfileSaved + ", bounded=" + bounded + ", overflow=" + overflowLogs +
                    ", hooks=" + report.profilingHookFailures + ", sampleCap=" + report.sampleCapacityReached + ", anchors=" + anchors + ", calibrated=" + calibrated +
                    ", scopes=" + report.stepOccurrences + ", executed=" + report.executedSteps + ", identityChanged=" + report.threadIdentityChanged +
                    ", targetFrameMissing=" + report.requiredTargetThreadFrameMissing + ", rawFailure=" + report.firstRawReadFailure;
                report.mainThreadAllocationAvailable = rawValid && threads[0].allocationAvailable && report.stepOccurrences == MeasuredSteps && report.executedSteps == MeasuredSteps;
                if (report.mainThreadAllocationAvailable) report.scopedMainThreadAllocatedBytes = scoped;
                report.capturedGcSpans = new GcSpan[gcSpanCount]; Array.Copy(gcSpans, report.capturedGcSpans, gcSpanCount);
                // Keep the original Load contract: startup/calibration spans cannot make an unknown Load GC measurement pass.
                report.gcSpans = ClipGcSpans(gcSpans, gcSpanCount, begin, end, false);
                report.gcMeasurementAvailable = report.allocationMeasurementAvailable && report.gcCalibrationMarkers > 0 &&
                    !report.gcSpanCapacityReached && (report.gcCollectionsInLoad == null ||
                        report.gcCollectionsInLoad[0] == 0 || report.gcSpans.Length > 0);
                if (report.gcMeasurementAvailable) report.observedGcCollectUnionMs = UnionMilliseconds(report.gcSpans, report.gcSpans.Length);
                CompletePhases(rawValid);
            }
            private void CompletePhases(bool rawValid)
            {
                report.phases = new PhaseReport[3];
                long[] before = { report.startupBeforeTicks, report.workloadInitBeforeTicks, report.loadStartBeforeTicks, report.loadEndBeforeTicks };
                long[] after = { report.startupAfterTicks, report.workloadInitAfterTicks, report.loadStartAfterTicks, report.loadEndAfterTicks };
                int[] occurrences = { report.startupOccurrences, report.workloadInitOccurrences, report.loadStartOccurrences, report.loadEndOccurrences };
                CollectionSnapshot[] counters = { report.startupCollections, report.workloadInitCollections, report.loadCollections, report.loadEndCollections };
                int count = 0;
                foreach (var thread in threads) if (thread != null) count++;
                for (int index = 0; index < report.phases.Length; index++)
                {
                    ulong first = PhaseBegin(index), last = PhaseEnd(index);
                    var phase = new PhaseReport { phase = PhaseName(index), beginNs = first, endNs = last,
                        beginBeforeTicks = before[index], beginAfterTicks = after[index], endBeforeTicks = before[index + 1], endAfterTicks = after[index + 1],
                        counterStart = counters[index], counterEnd = counters[index + 1], threads = new PhaseThreadReport[count] };
                    report.phases[index] = phase;
                    phase.anchorsValid = occurrences[index] == 1 && occurrences[index + 1] == 1 && first < last &&
                        0 < before[index] && before[index] <= after[index] && after[index] < before[index + 1] && before[index + 1] <= after[index + 1];
                    phase.clockMappingErrorBoundMs = PhaseClockErrorBound(first, last, before[index], after[index], before[index + 1], after[index + 1], Stopwatch.Frequency);
                    phase.queueClockCorrelationVerified = rawValid && phase.anchorsValid && phase.clockMappingErrorBoundMs <= 5;
                    bool allThreads = count == report.expectedThreads && count > 0;
                    int next = 0;
                    foreach (var thread in threads)
                    {
                        if (thread == null) continue;
                        var value = thread.phases[index]; phase.threads[next++] = value; value.threadId = thread.threadId;
                        value.lifetimeObserved = thread.calibration.occurrences > 0 && thread.calibration.beginNs < thread.calibration.endNs;
                        ulong birth = thread.role == "main" ? 0 : thread.calibration.endNs;
                        value.effectiveBeginNs = Math.Max(first, birth);
                        foreach (var frame in report.frames)
                            if (frame.role == "main" && PhaseNeedsThreadFrame(frame.beginNs, frame.endNs, first, last, birth))
                                value.requiredFrameMask |= 1UL << (frame.frame - report.firstFrame);
                        value.observedFrameMask = thread.frameMask;
                        value.missingFrames = CountBits(value.requiredFrameMask & ~thread.frameMask);
                        value.loadContractMatches = index != 2 || (value.observedBusinessBytes == thread.observedLoadBytes && value.excludedMeasurementSetupBytes == 0);
                        value.allocationAvailable = rawValid && phase.anchorsValid && value.lifetimeObserved && thread.calibrated &&
                            thread.calibration.missingMetadata == 0 && value.missingFrames == 0 && value.missingByteMetadata == 0 &&
                            value.missingSetupByteMetadata == 0 && value.loadContractMatches;
                        if (value.allocationAvailable) value.businessAllocatedBytes = value.observedBusinessBytes;
                        allThreads &= value.allocationAvailable;
                    }
                    phase.allocationMeasurementAvailable = allThreads;
                    phase.processGcCollections = report.gcCollectionCounterVerified ? Difference(counters[index + 1].counts, counters[index].counts) : null;
                    var businessGc = ClipGcSpans(gcSpans, gcSpanCount, first, last, false);
                    var calibrationGc = ClipGcSpans(gcSpans, gcSpanCount, first, last, true);
                    phase.businessGcSpanCount = businessGc.Length; phase.calibrationGcSpanCount = calibrationGc.Length;
                    phase.gcMeasurementAvailable = allThreads && report.gcCalibrationMarkers > 0 && !report.gcSpanCapacityReached &&
                        (phase.processGcCollections == null || phase.processGcCollections[0] == 0 || businessGc.Length > 0);
                    if (phase.gcMeasurementAvailable) phase.observedBusinessGcUnionMs = UnionMilliseconds(businessGc, businessGc.Length);
                    phase.reason = !phase.anchorsValid ? "MissingOrInvalidPhaseAnchors" : !allThreads ? "UnverifiedThreadLifetimeCalibrationOrFrames" :
                        !phase.queueClockCorrelationVerified ? "ClockCorrelationUnverified" : !phase.gcMeasurementAvailable ?
                        "AllocationVerified;ProcessGcCounterNotExplainedByObservedBusinessMarkers" : "PhaseAllocationAndObservedGcMarkersVerified";
                }
                report.initializationAllocationAvailable = report.phases[0].allocationMeasurementAvailable && report.phases[1].allocationMeasurementAvailable;
                report.initializationClockCorrelationVerified = report.phases[0].queueClockCorrelationVerified && report.phases[1].queueClockCorrelationVerified;
                report.calibrationGcSpans = ClipGcSpans(gcSpans, gcSpanCount, 0, end, true);
                report.calibrationGcMeasurementAvailable = rawValid && report.gcCalibrationMarkers > 0 && !report.gcSpanCapacityReached;
                if (report.calibrationGcMeasurementAvailable)
                    report.observedCalibrationGcUnionMs = UnionMilliseconds(report.calibrationGcSpans, report.calibrationGcSpans.Length);
            }
            public void InvalidatePhases(string reason)
            {
                report.initializationAllocationAvailable = false; report.initializationClockCorrelationVerified = false;
                report.calibrationGcMeasurementAvailable = false; report.observedCalibrationGcUnionMs = null;
                if (report.phases == null) return;
                foreach (var phase in report.phases)
                {
                    phase.allocationMeasurementAvailable = false; phase.gcMeasurementAvailable = false;
                    phase.queueClockCorrelationVerified = false; phase.observedBusinessGcUnionMs = null; phase.reason = reason;
                    foreach (var thread in phase.threads) { thread.allocationAvailable = false; thread.businessAllocatedBytes = null; }
                }
            }
            public void FinalizeProfilerErrors()
            {
                report.profilerOverflowLogs = Volatile.Read(ref overflowLogs);
                report.firstProfilerOverflowLog = firstOverflowLog;
                if (report.profilerOverflowLogs == 0) return;
                report.allocationMeasurementAvailable = false; report.mainThreadAllocationAvailable = false;
                report.gcMeasurementAvailable = false; report.scopedMainThreadAllocatedBytes = null;
                report.observedGcCollectUnionMs = null; report.measurementReason = "ProfilerBufferErrorIncludingSave:" + firstOverflowLog;
                InvalidatePhases("ProfilerBufferErrorIncludingSave");
                if (report.threads != null) foreach (var thread in report.threads)
                    if (thread != null) { thread.allocationAvailable = false; thread.loadAllocationBytes = null; }
            }
            private void FindAnchors()
            {
                using var iterator = new ProfilerFrameDataIterator();
                for (int frame = report.firstFrame; frame <= report.lastFrame; frame++)
                for (int index = 0; index < Math.Min(iterator.GetThreadCount(frame), MaximumRawThreads); index++)
                {
                    using var data = ProfilerDriver.GetRawFrameDataView(frame, index);
                    if (!data.valid) break;
                    if (data.threadName != "Main Thread") continue;
                    if (data.sampleCount >= MaximumSamples)
                    {
                        report.sampleCapacityReached = true;
                        report.firstRawReadFailure ??= "TargetSampleCapacity: anchors frame=" + frame + ", index=" + index + ", samples=" + data.sampleCount;
                        break;
                    }
                    int startId = data.GetMarkerId(prefix + ".LoadStart"), endId = data.GetMarkerId(prefix + ".LoadEnd");
                    int startupId = data.GetMarkerId(prefix + ".Startup"), workloadInitId = data.GetMarkerId(prefix + ".WorkloadInit");
                    for (int sample = 0; sample < data.sampleCount; sample++)
                    {
                        int id = data.GetSampleMarkerId(sample);
                        if (startupId != FrameDataView.invalidMarkerId && id == startupId) { report.startupOccurrences++; startupBegin = data.GetSampleStartTimeNs(sample); }
                        if (workloadInitId != FrameDataView.invalidMarkerId && id == workloadInitId) { report.workloadInitOccurrences++; workloadInitBegin = data.GetSampleStartTimeNs(sample); }
                        if (startId != FrameDataView.invalidMarkerId && id == startId) { report.loadStartOccurrences++; begin = data.GetSampleStartTimeNs(sample); }
                        if (endId != FrameDataView.invalidMarkerId && id == endId) { report.loadEndOccurrences++; end = data.GetSampleStartTimeNs(sample); }
                    }
                    break;
                }
            }
            private void ReadThread(RawFrameDataView data, ThreadReport thread, int frame, int index)
            {
                string id = data.threadId.ToString();
                if (thread.threadId != null && thread.threadId != id)
                {
                    report.threadIdentityChanged = true;
                    report.firstRawReadFailure ??= "TargetThreadIdentityChanged: frame=" + frame + ", index=" + index +
                        ", role=" + thread.role + ", expected=" + thread.threadId + ", actual=" + id;
                    return;
                }
                thread.threadId = id; thread.rawFrames++;
                thread.frameMask |= 1UL << (frame - report.firstFrame);
                if (thread.role == "main" && data.frameStartTimeNs < end && data.frameStartTimeNs + data.frameTimeNs > begin)
                    requiredLoadFrames |= 1UL << (frame - report.firstFrame);
                if (data.sampleCount >= MaximumSamples)
                {
                    report.sampleCapacityReached = true;
                    report.firstRawReadFailure ??= "TargetSampleCapacity: frame=" + frame + ", index=" + index +
                        ", role=" + thread.role + ", samples=" + data.sampleCount;
                    return;
                }
                int allocation = data.GetMarkerId("GC.Alloc"), stepId = data.GetMarkerId(prefix + ".Step");
                int workId = data.GetMarkerId(thread.workMarker), gcCalibrationId = data.GetMarkerId(prefix + ".GcCalibration");
                int calibrationId = data.GetMarkerId(thread.calibration.marker);
                // Locate this thread's complete measurement setup before classifying its allocation events.
                // Its business lifetime starts after setup, so frames before worker birth are not missing history.
                if (calibrationId != FrameDataView.invalidMarkerId)
                    for (int sample = 0; sample < data.sampleCount; sample++)
                        if (data.GetSampleMarkerId(sample) == calibrationId)
                        {
                            ulong regionBegin = data.GetSampleStartTimeNs(sample), regionEnd = regionBegin + data.GetSampleTimeNs(sample);
                            thread.calibration.beginNs = thread.calibration.occurrences == 0 ? regionBegin : Math.Min(thread.calibration.beginNs, regionBegin);
                            thread.calibration.endNs = Math.Max(thread.calibration.endNs, regionEnd);
                            thread.calibration.occurrences++;
                        }
                Scope[] scopes = { thread.empty, thread.small, thread.large };
                int[] ids = { data.GetMarkerId(scopes[0].marker), data.GetMarkerId(scopes[1].marker), data.GetMarkerId(scopes[2].marker) };
                int[] starts = { -1, -1, -1 }, ends = { -1, -1, -1 };
                int stepStart = -1, stepEnd = -1, gcStart = -1, gcEnd = -1;
                long loadBytes = 0; int loadEvents = 0, missing = 0, steps = 0;
                long frameCalibrationBytes = 0; int frameCalibrationMissing = 0;
                var phaseFrames = new PhaseFrameReport[3];
                for (int i = 0; i < phaseFrames.Length; i++) phaseFrames[i] = new PhaseFrameReport { phase = PhaseName(i) };
                for (int sample = 0; sample < data.sampleCount; sample++)
                {
                    int marker = data.GetSampleMarkerId(sample);
                    ulong at = data.GetSampleStartTimeNs(sample);
                    for (int scope = 0; scope < scopes.Length; scope++)
                        if (ids[scope] != FrameDataView.invalidMarkerId && marker == ids[scope])
                        { scopes[scope].occurrences++; starts[scope] = sample; ends[scope] = sample + data.GetSampleChildrenCountRecursive(sample); }
                    if (stepId != FrameDataView.invalidMarkerId && marker == stepId)
                    {
                        report.stepOccurrences++; steps++; stepStart = sample; stepEnd = sample + data.GetSampleChildrenCountRecursive(sample);
                    }
                    if (gcCalibrationId != FrameDataView.invalidMarkerId && marker == gcCalibrationId)
                    { gcStart = sample; gcEnd = sample + data.GetSampleChildrenCountRecursive(sample); }
                    if (workId != FrameDataView.invalidMarkerId && marker == workId)
                    {
                        ulong finish = at + data.GetSampleTimeNs(sample);
                        for (int phase = 0; phase < 3; phase++)
                        {
                            ulong phaseBegin = PhaseBegin(phase), phaseEnd = PhaseEnd(phase);
                            if (at < phaseEnd && finish > phaseBegin)
                            {
                                thread.phases[phase].workScopesOverlapping++;
                                phaseFrames[phase].workScopesOverlapping++;
                                if (at < phaseBegin || finish > phaseEnd)
                                {
                                    thread.phases[phase].workScopesCrossingBoundary++;
                                    phaseFrames[phase].workScopesCrossingBoundary++;
                                }
                            }
                        }
                        if (at < end && finish > begin)
                        {
                            thread.workScopesOverlappingLoad++;
                            if (at < begin || finish > end) thread.workScopesCrossingLoadBoundary++;
                        }
                    }
                    if (marker == allocation && allocation != FrameDataView.invalidMarkerId)
                    {
                        long? bytes = data.GetSampleMetadataCount(sample) > 0 ? data.GetSampleMetadataAsLong(sample, 0) : (long?)null;
                        if (bytes < 0) bytes = null;
                        bool calibrationEvent = InCalibration(at, thread.calibration.beginNs, thread.calibration.endNs);
                        bool measurementSetup = IsMeasurementSetup(at, thread.role != "main", thread.calibration.beginNs, thread.calibration.endNs);
                        if (calibrationEvent)
                        {
                            thread.calibration.allocationEvents++;
                            if (bytes.HasValue) { thread.calibration.bytes += bytes.Value; frameCalibrationBytes += bytes.Value; }
                            else { thread.calibration.missingMetadata++; frameCalibrationMissing++; }
                        }
                        int phaseIndex = PhaseAt(at, startupBegin, workloadInitBegin, begin, end);
                        if (phaseIndex >= 0)
                        {
                            var phase = thread.phases[phaseIndex];
                            var phaseFrame = phaseFrames[phaseIndex];
                            if (bytes.HasValue) { phase.totalKnownBytes += bytes.Value; phaseFrame.totalKnownBytes += bytes.Value; }
                            if (measurementSetup)
                            {
                                phase.excludedSetupAllocationEvents++; phaseFrame.excludedSetupAllocationEvents++;
                                if (bytes.HasValue)
                                {
                                    phase.excludedMeasurementSetupBytes += bytes.Value; phaseFrame.excludedMeasurementSetupBytes += bytes.Value;
                                    if (calibrationEvent) { phase.calibrationBytes += bytes.Value; phaseFrame.calibrationBytes += bytes.Value; }
                                }
                                else { phase.missingSetupByteMetadata++; phaseFrame.missingSetupByteMetadata++; }
                            }
                            else
                            {
                                phase.allocationEvents++; phaseFrame.allocationEvents++;
                                if (bytes.HasValue) { phase.observedBusinessBytes += bytes.Value; phaseFrame.observedBusinessBytes += bytes.Value; }
                                else { phase.missingByteMetadata++; phaseFrame.missingByteMetadata++; }
                            }
                        }
                        for (int scope = 0; scope < scopes.Length; scope++)
                            if (sample > starts[scope] && sample <= ends[scope])
                            { if (bytes.HasValue) scopes[scope].bytes += bytes.Value; else scopes[scope].missingMetadata++; }
                        if (at >= begin && at <= end)
                        {
                            loadEvents++;
                            if (bytes.HasValue) loadBytes += bytes.Value; else missing++;
                            if (sample > stepStart && sample <= stepEnd && bytes.HasValue) thread.stepBytes += bytes.Value;
                        }
                    }
                    else
                    {
                        string name = data.GetSampleName(sample);
                        if (name == null || (name.IndexOf("GC.Collect", StringComparison.Ordinal) < 0 &&
                            name.IndexOf("GarbageCollector.Collect", StringComparison.Ordinal) < 0)) continue;
                        if (sample > gcStart && sample <= gcEnd) report.gcCalibrationMarkers++;
                        ulong finish = at + data.GetSampleTimeNs(sample);
                        if (at >= end || finish <= at) continue;
                        if (gcSpanCount == MaximumGcSpans) { report.gcSpanCapacityReached = true; continue; }
                        gcSpans[gcSpanCount++] = new GcSpan { threadId = id, marker = name,
                            beginNs = at, endNs = Math.Min(finish, end),
                            calibration = InCalibration(at, thread.calibration.beginNs, thread.calibration.endNs) };
                    }
                }
                thread.observedLoadBytes += loadBytes; thread.loadAllocationEvents += loadEvents; thread.missingByteMetadata += missing;
                // At most MaximumFrames * MaximumThreads aggregate rows; never retain one object per allocation event.
                report.frames.Add(new FrameReport { frame = frame, role = thread.role, threadId = id, samples = data.sampleCount,
                    allocationEvents = loadEvents, bytes = loadBytes, missingMetadata = missing, stepScopes = steps,
                    beginNs = data.frameStartTimeNs, endNs = data.frameStartTimeNs + data.frameTimeNs, phases = phaseFrames,
                    calibrationBytes = frameCalibrationBytes, calibrationMissingMetadata = frameCalibrationMissing });
            }

            private ulong PhaseBegin(int phase) => phase == 0 ? startupBegin : phase == 1 ? workloadInitBegin : begin;
            private ulong PhaseEnd(int phase) => phase == 0 ? workloadInitBegin : phase == 1 ? begin : end;
        }

        internal static bool MeasurementValid(bool saved, bool bounded, bool capacityOkay, bool anchors, bool calibrated, int scopes, int steps)
            => saved && bounded && capacityOkay && anchors && calibrated && scopes == MeasuredSteps && steps == MeasuredSteps;
        internal static bool TargetThreadFrameComplete(bool mainSeen, bool intersectsLoad, int matchedTargets, int expectedTargets)
            => mainSeen && (!intersectsLoad || matchedTargets == expectedTargets);
        internal static int PhaseAt(ulong at, ulong startup, ulong initialization, ulong load, ulong end)
        {
            if (!(startup < initialization && initialization < load && load < end) || at < startup || at > end) return -1;
            return at < initialization ? 0 : at < load ? 1 : 2;
        }
        internal static bool InCalibration(ulong at, ulong start, ulong end) => start < end && at >= start && at < end;
        internal static bool IsMeasurementSetup(ulong at, bool worker, ulong start, ulong end)
            => InCalibration(at, start, end) || (worker && start < end && at < end);
        internal static bool PhaseNeedsThreadFrame(ulong frameStart, ulong frameEnd, ulong phaseStart, ulong phaseEnd, ulong businessBirth)
            => Math.Max(phaseStart, businessBirth) < phaseEnd && frameStart < phaseEnd && frameEnd > Math.Max(phaseStart, businessBirth);
        private static string PhaseName(int phase) => phase == 0 ? "startup" : phase == 1 ? "workloadInit" : "load";
        internal static double PhaseClockErrorBound(ulong rawStart, ulong rawEnd, long before, long after, long endBefore, long endAfter, long frequency)
        {
            double rate = 1e9 / frequency;
            double first = rawStart - (before * .5 + after * .5) * rate;
            double last = rawEnd - (endBefore * .5 + endAfter * .5) * rate;
            return (Math.Abs(last - first) + Math.Max(after - before, endAfter - endBefore) * rate / 2) / 1e6;
        }
        private static bool CalibrationPassed(Scope empty, Scope small, Scope large)
            => empty.occurrences == 1 && small.occurrences == 1 && large.occurrences == 1 &&
                empty.missingMetadata == 0 && small.missingMetadata == 0 && large.missingMetadata == 0 &&
                empty.bytes == 0 && small.bytes >= 32768 && large.bytes >= 65536 && large.bytes - small.bytes == 32768;
        private static double UnionMilliseconds(GcSpan[] spans, int count)
        {
            if (count == 0) return 0;
            Array.Sort(spans, 0, count, Comparer<GcSpan>.Create((a, b) => a.beginNs.CompareTo(b.beginNs)));
            ulong start = spans[0].beginNs, end = spans[0].endNs, total = 0;
            for (int i = 1; i < count; i++)
            {
                if (spans[i].beginNs <= end) end = Math.Max(end, spans[i].endNs);
                else { total += end - start; start = spans[i].beginNs; end = spans[i].endNs; }
            }
            return (total + end - start) / 1e6;
        }
        private static GcSpan[] ClipGcSpans(GcSpan[] spans, int count, ulong begin, ulong end, bool calibration)
        {
            int matches = 0;
            for (int i = 0; i < count; i++)
                if (spans[i].calibration == calibration && spans[i].beginNs < end && spans[i].endNs > begin) matches++;
            var result = new GcSpan[matches];
            int next = 0;
            for (int i = 0; i < count; i++)
            {
                var span = spans[i];
                if (span.calibration != calibration || span.beginNs >= end || span.endNs <= begin) continue;
                result[next++] = new GcSpan { beginNs = Math.Max(begin, span.beginNs), endNs = Math.Min(end, span.endNs),
                    threadId = span.threadId, marker = span.marker, calibration = calibration };
            }
            return result;
        }
        private static int CountBits(ulong bits) { int count = 0; while (bits != 0) { bits &= bits - 1; count++; } return count; }
        private static Type Nested(Type owner, string name) => owner.GetNestedType(name, BindingFlags.NonPublic) ?? throw new MissingMemberException(owner.FullName, name);
        private static long Dropped(List<CombatEvidenceStore> stores) { long result = 0; foreach (var store in stores) result += store.Dropped; return result; }
        private static int[] Collections() => new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
        private static int[] Difference(int[] a, int[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        private static string CoveragePath(string root) => Path.Combine(root, Run, "1", "sources", Capture, "coverage.json");
        internal static EvidenceCoverage PollCoverageFile(string path)
        {
            string json;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                json = reader.ReadToEnd();
            }
            catch (IOException) { return null; }
            // Atomic replacement can race a live poll. A failed read is not a flush acknowledgement;
            // malformed content must still fail, and the caller retains its original deadline and cadence.
            return EvidenceJson.Decode<EvidenceCoverage>(json);
        }
        private static EvidenceCoverage Coverage(string root)
        {
            string path = CoveragePath(root);
            return File.Exists(path) ? EvidenceJson.Decode<EvidenceCoverage>(File.ReadAllText(path)) : null;
        }
        private sealed class BoundWorkload
        {
            public readonly Action<int> Step; public readonly Func<string> Digest;
            private readonly long[] ticks;
            public BoundWorkload(Type benchmark, int count)
            {
                Type type = Nested(benchmark, "Workload");
                object instance = Activator.CreateInstance(type, count, WorkloadDuration);
                Step = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), instance, type.GetMethod("Step"));
                Digest = (Func<string>)Delegate.CreateDelegate(typeof(Func<string>), instance, type.GetMethod("Digest"));
                ticks = (long[])type.GetField("tickCounts", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance);
            }
            public long TotalTicks() { long sum = 0; foreach (long count in ticks) sum += count; return sum; }
        }
        private sealed class WorkerContext { public Probe owner; public ThreadReport thread; public bool open; }
        private sealed class Scope { public string marker; public int occurrences, missingMetadata; public long bytes; }
        private sealed class CalibrationRegion
        {
            public string marker;
            public int occurrences, missingMetadata, allocationEvents;
            public ulong beginNs, endNs;
            public long bytes;
        }
        private sealed class CollectionSnapshot
        {
            public long beforeTicks, afterTicks;
            public int[] counts = new int[3];
            public void Capture()
            {
                beforeTicks = Stopwatch.GetTimestamp();
                for (int i = 0; i < counts.Length; i++) counts[i] = GC.CollectionCount(i);
                afterTicks = Stopwatch.GetTimestamp();
            }
        }
        private sealed class PhaseThreadReport
        {
            public string phase, role, threadId;
            public ulong effectiveBeginNs, requiredFrameMask, observedFrameMask;
            public long observedBusinessBytes, excludedMeasurementSetupBytes, calibrationBytes, totalKnownBytes;
            public long? businessAllocatedBytes;
            public int allocationEvents, excludedSetupAllocationEvents, missingByteMetadata, missingSetupByteMetadata,
                missingFrames, workScopesOverlapping, workScopesCrossingBoundary;
            public bool lifetimeObserved, allocationAvailable, loadContractMatches;
        }
        private sealed class PhaseReport
        {
            public string phase, reason;
            public ulong beginNs, endNs;
            public long beginBeforeTicks, beginAfterTicks, endBeforeTicks, endAfterTicks;
            public double clockMappingErrorBoundMs;
            public bool anchorsValid, queueClockCorrelationVerified, allocationMeasurementAvailable, gcMeasurementAvailable;
            public int[] processGcCollections;
            public CollectionSnapshot counterStart, counterEnd;
            public double? observedBusinessGcUnionMs;
            public int businessGcSpanCount, calibrationGcSpanCount;
            public PhaseThreadReport[] threads;
        }
        private sealed class ThreadReport
        {
            public string role, threadName, threadId, workMarker;
            public Scope empty, small, large;
            public CalibrationRegion calibration;
            public PhaseThreadReport[] phases = new PhaseThreadReport[3];
            public int rawFrames, missingLoadFrames, missingByteMetadata, loadAllocationEvents, workScopesOverlappingLoad, workScopesCrossingLoadBoundary;
            public ulong frameMask;
            public long observedLoadBytes, stepBytes;
            public long? loadAllocationBytes;
            public bool calibrated, allocationAvailable;
            public ThreadReport(string prefix, string role, string name)
            {
                this.role = role; threadName = name;
                string key = prefix + "." + role;
                empty = new Scope { marker = key + ".Empty" }; small = new Scope { marker = key + ".32KiB" };
                large = new Scope { marker = key + ".64KiB" }; workMarker = key + ".Work";
                calibration = new CalibrationRegion { marker = key + ".Calibration" };
                for (int i = 0; i < phases.Length; i++) phases[i] = new PhaseThreadReport { phase = PhaseName(i), role = role };
            }
        }
        private sealed class GcSpan { public ulong beginNs, endNs; public string threadId, marker; public bool calibration; }
        private sealed class FrameReport
        {
            public int frame, samples, allocationEvents, missingMetadata, stepScopes;
            public string role, threadId; public long bytes;
            public ulong beginNs, endNs;
            public long calibrationBytes;
            public int calibrationMissingMetadata;
            public PhaseFrameReport[] phases;
        }
        private sealed class PhaseFrameReport
        {
            public string phase;
            public long observedBusinessBytes, excludedMeasurementSetupBytes, calibrationBytes, totalKnownBytes;
            public int allocationEvents, excludedSetupAllocationEvents, missingByteMetadata, missingSetupByteMetadata,
                workScopesOverlapping, workScopesCrossingBoundary;
        }
        private sealed class RawThreadScanReport
        {
            public int frame, totalRawThreads, scannedThreads, matchedTargets, expectedTargets;
            public bool mainSeen, intersectsLoad, scanTruncated, requiredTargetsComplete;
        }
        private sealed class CaseReport
        {
            public bool deepProfilingEnabled, allocationCallstacksEnabled;
            public int schemaVersion = 2, controllers, repeat, targetHz = Frequency, measuredSimulationSteps = MeasuredSteps,
                firstMeasuredSimulationStep = 0, stepsPerUnityFrame = StepsPerUnityFrame, executedSteps, expectedThreads,
                firstFrame, lastFrame, rawFrameCount, stepOccurrences, loadStartOccurrences, loadEndOccurrences,
                profilerOverflowLogs, gcCalibrationMarkers, startupOccurrences, workloadInitOccurrences;
            public int rawFrameCapacity = MaximumFrames, perThreadFrameSampleCapacity = MaximumSamples, gcSpanCapacity = MaximumGcSpans;
            public int rawThreadScanCapacity = MaximumRawThreads, totalRawThreadsPeak;
            public long profilerBufferBytes = ProfilerBufferBytes;
            public long profilingHookFailuresBefore, profilingHookFailures;
            public long stopwatchFrequency = Stopwatch.Frequency, loadStartBeforeTicks, loadStartAfterTicks, loadEndBeforeTicks, loadEndAfterTicks;
            public long startupBeforeTicks, startupAfterTicks, workloadInitBeforeTicks, workloadInitAfterTicks;
            public ulong rawStartupStartNs, rawWorkloadInitStartNs, rawLoadStartNs, rawLoadEndNs;
            public double profilerMinusStopwatchStartNs, profilerMinusStopwatchEndNs, clockMappingErrorBoundMs;
            public string mode, directory, failure, expectedHash, actualHash, measurementReason, rawProfilePath, firstProfilerOverflowLog, firstRawReadFailure;
            public bool allocationMeasurementAvailable, gcMeasurementAvailable, businessMatches, catchupComplete,
                rawProfileSaved, sampleCapacityReached, gcSpanCapacityReached, mainThreadAllocationAvailable, gcCollectionCounterVerified,
                queueClockCorrelationVerified, workerCloseFailed, rawThreadScanTruncated, threadIdentityChanged,
                requiredTargetThreadFrameMissing, invalidRawThreadView, duplicateTargetThreadView;
            public bool initializationAllocationAvailable, initializationClockCorrelationVerified, calibrationGcMeasurementAvailable;
            public long? scopedMainThreadAllocatedBytes;
            public double? observedGcCollectUnionMs;
            public double? observedCalibrationGcUnionMs;
            public long dotTicksInWindow, measuredWindowDropped, diagnosticDropped, budgetReservationPeakBytes, producedRecords;
            public double measuredWindowWallSeconds, achievedHz, anchorWindowSeconds, catchupWallSeconds, postLoadCaptureTailSeconds;
            public int[] gcCollectionsInLoad, gcCounterCalibrationBefore, gcCounterCalibrationAfter;
            public List<string> writerFailures = new(2), replicationFailures = new(2), observationFailures = new(2);
            public List<EvidenceCoverage> coverage = new(2);
            public List<FrameReport> frames = new(MaximumFrames * MaximumThreads);
            public List<RawThreadScanReport> rawThreadScans = new(MaximumFrames);
            public ThreadReport[] threads; public GcSpan[] gcSpans;
            public PhaseReport[] phases;
            public GcSpan[] capturedGcSpans, calibrationGcSpans;
            public CollectionSnapshot startupCollections = new(), workloadInitCollections = new(), loadCollections = new(), loadEndCollections = new();
            public string workloadSource = "Existing CombatEvidenceLoadBenchmarks.Workload/ProbeSink/Hub/Endpoint; same 720 steps 0..719 and duration 5.";
            public string scope = "Profiler-only PlayMode allocation/GC evidence. Main Step bytes exclude pacing, reflection, setup and ending checkpoint; " +
                "per-thread load bytes use main load timestamp anchors and may include harness/initial backlog on that thread. Worker scopes crossing boundaries " +
                "are counted separately; bytes are timestamp-clipped to the load window. GC collection counts are process-wide; observed GC marker intervals " +
                "are unioned, never summed across threads. Profiler timings are excluded from the normal CPU gate. Catch-up allocations are unmeasured. " +
                "Catch-up deadline starts before the ending checkpoint and includes two raw-frame completion yields (postLoadCaptureTailSeconds); " +
                "its scheduling has profiler overhead and is not a normal CPU/storage timing acceptance. Raw save/analysis occurs after catch-up and close. " +
                "Raw thread discovery scans at most 128 entries per frame and stops after finding the registered 1/2/5 targets. Total Unity thread count " +
                "and truncated scans are reported separately from per-target sample saturation. Startup frames may precede worker registration; every " +
                "load frame must contain all target threads. An invalid view encountered during discovery is recorded; missing required targets invalidate the measurement. " +
                "Startup starts before Store creation; workloadInit starts before the real BoundWorkload constructor. Each phase has its own two clock anchors. " +
                "Calibration and worker measurement setup bytes are excluded from business phase totals and reported separately. Worker phase history is required " +
                "only after its calibration completes; earlier frames do not predate an observed business lifetime. Calibration still changes heap state and scheduling. " +
                "Business phase totals still include ordinary probe orchestration and diagnostic observer overhead; they do not attribute bytes to an individual payload. " +
                "Phase GC counters are process-wide observations between their saved counter-read bounds, not pause time or ownership evidence; calibration may overlap startup. " +
                "Only known non-calibration GC marker intervals are unioned as observed business GC time; unexplained nonzero counters keep that time unavailable. " +
                "This does not measure retained diagnostic objects, native allocations, Steam, rendering or normal frame performance.";
        }
    }

    public sealed class CombatProfilerAllocationEvidenceTests
    {
        [Test]
        public void StartupAndWorkloadInitializationHaveIndependentClockAnchors()
        {
            var report = typeof(CombatWorkloadAllocationPlayModeTests).GetNestedType("CaseReport", BindingFlags.NonPublic);
            Assert.That(report, Is.Not.Null);
            foreach (string field in new[] { "startupBeforeTicks", "startupAfterTicks", "workloadInitBeforeTicks", "workloadInitAfterTicks" })
                Assert.That(report.GetField(field, BindingFlags.Instance | BindingFlags.Public), Is.Not.Null, field);
        }
        [Test]
        public void InitializationPhasesPartitionEventsWithoutExtendingTheLoadWindow()
        {
            Assert.That(CombatWorkloadAllocationPlayModeTests.PhaseAt(9, 10, 20, 30, 40), Is.EqualTo(-1));
            Assert.That(CombatWorkloadAllocationPlayModeTests.PhaseAt(10, 10, 20, 30, 40), Is.EqualTo(0));
            Assert.That(CombatWorkloadAllocationPlayModeTests.PhaseAt(20, 10, 20, 30, 40), Is.EqualTo(1));
            Assert.That(CombatWorkloadAllocationPlayModeTests.PhaseAt(30, 10, 20, 30, 40), Is.EqualTo(2));
            Assert.That(CombatWorkloadAllocationPlayModeTests.PhaseAt(40, 10, 20, 30, 40), Is.EqualTo(2));
            Assert.That(CombatWorkloadAllocationPlayModeTests.PhaseAt(41, 10, 20, 30, 40), Is.EqualTo(-1));
            Assert.That(CombatWorkloadAllocationPlayModeTests.PhaseAt(20, 10, 20, 20, 40), Is.EqualTo(-1));
        }
        [Test]
        public void WorkerHistoryIsRequiredOnlyAfterItsObservedCalibrationLifetime()
        {
            Assert.That(CombatWorkloadAllocationPlayModeTests.PhaseNeedsThreadFrame(0, 10, 0, 20, 15), Is.False);
            Assert.That(CombatWorkloadAllocationPlayModeTests.PhaseNeedsThreadFrame(10, 20, 0, 20, 15), Is.True);
            Assert.That(CombatWorkloadAllocationPlayModeTests.PhaseNeedsThreadFrame(20, 30, 20, 30, 15), Is.True);
            Assert.That(CombatWorkloadAllocationPlayModeTests.PhaseNeedsThreadFrame(0, 10, 0, 10, 15), Is.False);
            Assert.That(CombatWorkloadAllocationPlayModeTests.PhaseNeedsThreadFrame(0, 10, 0, 20, 0), Is.True);
        }
        [Test]
        public void ArtificialCalibrationAndWorkerRegistrationAreExcludedFromBusinessBytes()
        {
            Assert.That(CombatWorkloadAllocationPlayModeTests.IsMeasurementSetup(15, false, 10, 20), Is.True);
            Assert.That(CombatWorkloadAllocationPlayModeTests.IsMeasurementSetup(5, false, 10, 20), Is.False);
            Assert.That(CombatWorkloadAllocationPlayModeTests.IsMeasurementSetup(5, true, 10, 20), Is.True);
            Assert.That(CombatWorkloadAllocationPlayModeTests.IsMeasurementSetup(20, true, 10, 20), Is.False);
        }
        [Test]
        public void StartupClockUncertaintyCannotBorrowTheLoadMapping()
        {
            double initialization = CombatWorkloadAllocationPlayModeTests.PhaseClockErrorBound(100000100, 120000100, 100, 102, 5000100, 5000102, 1000000000);
            double load = CombatWorkloadAllocationPlayModeTests.PhaseClockErrorBound(120000100, 125000100, 5000100, 5000102, 10000100, 10000102, 1000000000);
            Assert.That(initialization, Is.GreaterThan(5));
            Assert.That(load, Is.LessThan(0.001));
        }
        [Test]
        public void GcPhaseUnionExcludesCalibrationAndCountsOverlapsOnce()
        {
            var owner = typeof(CombatWorkloadAllocationPlayModeTests);
            var spanType = owner.GetNestedType("GcSpan", BindingFlags.NonPublic);
            var spans = Array.CreateInstance(spanType, 3);
            for (int i = 0; i < 3; i++)
            {
                object span = Activator.CreateInstance(spanType, true);
                spanType.GetField("beginNs").SetValue(span, i == 0 ? 10UL : i == 1 ? 20UL : 0UL);
                spanType.GetField("endNs").SetValue(span, i == 0 ? 30UL : i == 1 ? 40UL : 100UL);
                spanType.GetField("calibration").SetValue(span, i == 2);
                spans.SetValue(span, i);
            }
            var clipped = (Array)owner.GetMethod("ClipGcSpans", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { spans, 3, 20UL, 35UL, false });
            var union = (double)owner.GetMethod("UnionMilliseconds", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { clipped, clipped.Length });
            Assert.That(clipped.Length, Is.EqualTo(2)); Assert.That(union, Is.EqualTo(15 / 1e6));
        }
        [Test]
        public void FullWindowRequiresEveryStepAndCalibratedThreads()
        {
            Assert.That(CombatWorkloadAllocationPlayModeTests.MeasurementValid(true, true, true, true, true, 720, 720), Is.True);
            Assert.That(CombatWorkloadAllocationPlayModeTests.MeasurementValid(true, true, true, true, false, 720, 720), Is.False);
            Assert.That(CombatWorkloadAllocationPlayModeTests.MeasurementValid(true, true, true, true, true, 719, 720), Is.False);
            Assert.That(CombatWorkloadAllocationPlayModeTests.MeasurementValid(true, true, true, true, true, 720, 719), Is.False);
        }
        [Test]
        public void LostHistoryOrSaturatedRawDataCannotBecomeZeroAllocation()
        {
            Assert.That(CombatWorkloadAllocationPlayModeTests.MeasurementValid(true, false, true, true, true, 720, 720), Is.False);
            Assert.That(CombatWorkloadAllocationPlayModeTests.MeasurementValid(true, true, false, true, true, 720, 720), Is.False);
            Assert.That(CombatWorkloadAllocationPlayModeTests.MeasurementValid(false, true, true, true, true, 720, 720), Is.False);
        }
        [Test]
        public void CompleteRegisteredTargetsDoNotRequireUnrelatedUnityWorkerThreads()
        {
            // The archived failure had 139 Unity threads but its sole target was Main Thread at index 0.
            Assert.That(CombatWorkloadAllocationPlayModeTests.TargetThreadFrameComplete(true, true, 1, 1), Is.True);
            Assert.That(CombatWorkloadAllocationPlayModeTests.TargetThreadFrameComplete(true, true, 5, 5), Is.True);
            Assert.That(CombatWorkloadAllocationPlayModeTests.TargetThreadFrameComplete(true, true, 4, 5), Is.False);
        }
        [Test]
        public void StartupMayPrecedeWorkersButEveryLoadFrameRequiresTheirHistory()
        {
            Assert.That(CombatWorkloadAllocationPlayModeTests.TargetThreadFrameComplete(true, false, 1, 5), Is.True);
            Assert.That(CombatWorkloadAllocationPlayModeTests.TargetThreadFrameComplete(true, true, 1, 5), Is.False);
            Assert.That(CombatWorkloadAllocationPlayModeTests.TargetThreadFrameComplete(false, false, 0, 5), Is.False);
        }
        [Test]
        public void MissingLiveCoverageRemainsUnflushedUntilALaterPollReadsIt()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.Delete(path);
                Assert.That(CombatWorkloadAllocationPlayModeTests.PollCoverageFile(path), Is.Null);
                File.WriteAllText(path, EvidenceJson.Encode(new EvidenceCoverage { flushed = "17" }));
                Assert.That(CombatWorkloadAllocationPlayModeTests.PollCoverageFile(path)?.flushed, Is.EqualTo("17"));
            }
            finally { File.Delete(path); }
        }
        [Test]
        public void LockedLiveCoverageDoesNotThrowOrAcknowledgeAndSharedWriterCanBeRead()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, EvidenceJson.Encode(new EvidenceCoverage { flushed = "17" }));
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Assert.That(CombatWorkloadAllocationPlayModeTests.PollCoverageFile(path), Is.Null);
                using (var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
                    Assert.That(CombatWorkloadAllocationPlayModeTests.PollCoverageFile(path)?.flushed, Is.EqualTo("17"));
            }
            finally { File.Delete(path); }
        }
        [Test]
        public void MalformedLiveCoverageIsNotSwallowedAsARetryableFileRace()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "{broken");
                var error = Assert.Catch<Exception>(() => CombatWorkloadAllocationPlayModeTests.PollCoverageFile(path));
                Assert.That(error.GetType().FullName, Is.EqualTo("Newtonsoft.Json.JsonReaderException"));
            }
            finally { File.Delete(path); }
        }
    }
}
#endif
