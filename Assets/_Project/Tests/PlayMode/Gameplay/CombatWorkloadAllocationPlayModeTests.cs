#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using NUnit.Framework;
using Unity.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using RawProbe = MonsterSupergroup.Gameplay.Tests.CombatRawAllocationMeasurementPlayModeTests;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class CombatWorkloadAllocationPlayModeTests
    {
        private const int Frequency = 144, WarmupSteps = 144, MeasuredSteps = 144, StepsPerUnityFrame = 24;
        private const double WorkloadDuration = 4;
        private const string Run = "load";
        private static readonly string Capture = new('a', 32), RemoteCapture = new('b', 32);

        [UnityTest]
        [Category("CombatEvidenceAllocationWorkload")]
        public IEnumerator ExistingBenchmarkWorkloadHasCalibratedMainThreadAllocationBytes()
        {
            if (!Application.isBatchMode || Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_RAW_WORKLOAD_PROBE") != "1")
                Assert.Ignore("Requires isolated batch-mode Unity and COMBAT_EVIDENCE_RAW_WORKLOAD_PROBE=1; clears that process's profiler history.");
            string root = Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_ALLOCATION_PROBE_OUTPUT") ??
                Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/CombatEvidenceNextValidation/allocation-workload"));
            Directory.CreateDirectory(root);
            Type benchmark = Assembly.Load("MonsterSupergroup.NetworkCombat.Tests.EditMode").GetType(
                "MonsterSupergroup.NetworkCombat.Tests.CombatEvidenceLoadBenchmarks", true);
            var reports = new List<CaseReport>();
            foreach (int count in new[] { 50, 200, 500 })
            foreach (string mode in new[] { "off", "local", "replicated" })
            {
                var report = new CaseReport { controllers = count, mode = mode };
                string directory = Path.Combine(root, count + "-" + mode + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(directory); report.directory = directory;
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
                if (report.failure != null)
                {
                    report.allocationMeasurementAvailable = false; report.scopedMainThreadAllocatedBytes = null;
                    report.singleCheckpointMainThreadAllocatedBytes = null;
                }
                File.WriteAllText(Path.Combine(directory, "allocation.json"), EvidenceJson.Encode(report));
                reports.Add(report);
                File.WriteAllText(Path.Combine(root, "allocation-workload.json"), EvidenceJson.Encode(new { schemaVersion = 1, cases = reports }));
            }
            foreach (var report in reports)
            {
                Assert.That(report.failure, Is.Null, report.directory);
                Assert.That(report.businessMatches, Is.True, report.directory);
                Assert.That(report.diagnosticDropped, Is.Zero, report.directory);
                Assert.That(report.dotTicksInWindow, Is.GreaterThan(0), report.directory);
                Assert.That(report.allocationMeasurementAvailable, Is.True, report.directory);
            }
        }

        private static IEnumerator RunCase(Type benchmark, CaseReport report)
        {
            var priorSink = CombatEvidence.Sink;
            var stores = new List<CombatEvidenceStore>(); var replicators = new List<DiagnosticReplicator>();
            bool priorEnabled = ProfilerDriver.enabled, priorRuntimeEnabled = Profiler.enabled, priorEditor = ProfilerDriver.profileEditor;
            bool priorCpu = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU), priorMemory = ProfilerDriver.IsAreaEnabled(ProfilerArea.Memory);
            try
            {
                CombatEvidence.Sink = null;
                var expected = new BoundWorkload(benchmark, report.controllers);
                for (int frame = 0; frame < WarmupSteps + MeasuredSteps; frame++) expected.Step(frame);
                report.expectedHash = expected.Digest();
                object sink = null;
                var memory = new DiagnosticMemoryBudget();
                if (report.mode != "off")
                {
                    stores.Add(new CombatEvidenceStore(Path.Combine(report.directory, "primary"), new EvidenceStoreOptions { Memory = memory }));
                    sink = Activator.CreateInstance(Nested(benchmark, "ProbeSink"), stores[0]);
                    CombatEvidence.Sink = (IDiagnosticSink)sink;
                    if (report.mode == "replicated")
                    {
                        stores.Add(new CombatEvidenceStore(Path.Combine(report.directory, "remote"), new EvidenceStoreOptions { Memory = memory }));
                        object hub = Activator.CreateInstance(Nested(benchmark, "Hub"), true);
                        Type endpoint = Nested(benchmark, "Endpoint");
                        replicators.Add(new DiagnosticReplicator(stores[0], (IDiagnosticReplicationTransport)Activator.CreateInstance(endpoint, hub, 0), Capture));
                        replicators.Add(new DiagnosticReplicator(stores[1], (IDiagnosticReplicationTransport)Activator.CreateInstance(endpoint, hub, 1), RemoteCapture));
                        stores[1].TryWrite(new DiagnosticRecord { captureId = RemoteCapture, runId = Run, round = 1,
                            recordSequence = "1", role = "Process", stage = "process.start", outcome = "Started", input = "Synthetic remote receiver", critical = true });
                    }
                }
                var workload = new BoundWorkload(benchmark, report.controllers);
                FieldInfo sinkFrame = sink?.GetType().GetField("Frame"), sinkTime = sink?.GetType().GetField("NetworkTime");
                var pace = (Action<Stopwatch, double>)Delegate.CreateDelegate(typeof(Action<Stopwatch, double>),
                    benchmark.GetMethod("Pace", BindingFlags.NonPublic | BindingFlags.Static));
                Action checkpoint = sink == null ? null : (Action)Delegate.CreateDelegate(typeof(Action), sink,
                    sink.GetType().GetMethod("Checkpoint", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null));
                var warmupClock = Stopwatch.StartNew();
                for (int frame = 0; frame < WarmupSteps; frame++)
                {
                    sinkFrame?.SetValue(sink, frame); sinkTime?.SetValue(sink, frame / (double)Frequency);
                    pace(warmupClock, frame / (double)Frequency);
                    workload.Step(frame);
                    foreach (var replicator in replicators) replicator.Tick(Time.realtimeSinceStartupAsDouble, Run);
                    if ((frame + 1) % StepsPerUnityFrame == 0) yield return null;
                }
                report.initialAndWarmupDropped = Dropped(stores);
                long ticksBefore = workload.TotalTicks();
                string prefix = "CombatEvidence.AllocationWorkload." + Guid.NewGuid().ToString("N");
                var empty = new ProfilerMarker(prefix + ".Empty"); var small = new ProfilerMarker(prefix + ".32KiB");
                var large = new ProfilerMarker(prefix + ".64KiB"); var measured = new ProfilerMarker(prefix + ".StepAndReplication");
                var checkpointMarker = new ProfilerMarker(prefix + ".Checkpoint");
                report.raw = new RawProbe.Report { unityVersion = Application.unityVersion, rawProfilePath = Path.Combine(report.directory, "workload.raw"),
                    scopes = new[] { new RawProbe.Scope { marker = prefix + ".Empty" }, new RawProbe.Scope { marker = prefix + ".32KiB", payloadBytes = 32768 },
                        new RawProbe.Scope { marker = prefix + ".64KiB", payloadBytes = 65536 }, new RawProbe.Scope { marker = prefix + ".StepAndReplication" },
                        new RawProbe.Scope { marker = prefix + ".Checkpoint" } } };
                RawProbe.Allocate(32768); RawProbe.Allocate(65536);
                ProfilerDriver.enabled = false; Profiler.enabled = false; ProfilerDriver.ClearAllFrames();
                ProfilerDriver.profileEditor = false;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true); ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, true);
                Profiler.enabled = true; ProfilerDriver.enabled = true;
                yield return null;
                using (empty.Auto()) RawProbe.Allocate(0);
                using (small.Auto()) RawProbe.Allocate(32768);
                using (large.Auto()) RawProbe.Allocate(65536);
                var windowClock = Stopwatch.StartNew();
                for (int frame = WarmupSteps; frame < WarmupSteps + MeasuredSteps; frame++)
                {
                    // Reflection setup/boxing is excluded from the measured workload marker.
                    sinkFrame?.SetValue(sink, frame); sinkTime?.SetValue(sink, frame / (double)Frequency);
                    pace(windowClock, (frame - WarmupSteps) / (double)Frequency);
                    using (measured.Auto())
                    {
                        workload.Step(frame);
                        foreach (var replicator in replicators) replicator.Tick(Time.realtimeSinceStartupAsDouble, Run);
                    }
                    if ((frame + 1) % StepsPerUnityFrame == 0) yield return null;
                }
                report.measuredWindowWallSeconds = windowClock.Elapsed.TotalSeconds;
                report.measuredWindowDropped = Dropped(stores) - report.initialAndWarmupDropped;
                if (checkpoint != null) using (checkpointMarker.Auto()) checkpoint();
                for (int i = 0; i < 2; i++) yield return null;
                ProfilerDriver.enabled = false; Profiler.enabled = false;
                report.raw.firstFrame = ProfilerDriver.firstFrameIndex; report.raw.lastFrame = ProfilerDriver.lastFrameIndex;
                report.raw.frameCount = report.raw.firstFrame < 0 ? 0 : report.raw.lastFrame - report.raw.firstFrame + 1;
                if (report.raw.frameCount > 0 && report.raw.frameCount <= 10)
                {
                    report.raw.rawProfileSaved = ProfilerDriver.SaveProfile(report.raw.rawProfilePath) &&
                        File.Exists(report.raw.rawProfilePath) && new FileInfo(report.raw.rawProfilePath).Length > 0;
                    RawProbe.ReadFrames(report.raw);
                }
                else report.failure = "RawProfilerFrameCountOutside1To10:" + report.raw.frameCount;
                var scope = report.raw.scopes[3];
                report.allocationMeasurementAvailable = report.raw.allocationBytesVerified && scope.occurrences == MeasuredSteps && scope.missingByteMetadata == 0;
                if (report.allocationMeasurementAvailable) report.scopedMainThreadAllocatedBytes = scope.observedBytes;
                var checkpointScope = report.raw.scopes[4];
                if (report.raw.allocationBytesVerified && checkpointScope.occurrences == 1 && checkpointScope.missingByteMetadata == 0)
                    report.singleCheckpointMainThreadAllocatedBytes = checkpointScope.observedBytes;
                report.dotTicksInWindow = workload.TotalTicks() - ticksBefore;
                using (CombatEvidence.Suppress()) report.actualHash = workload.Digest();
                report.businessMatches = report.actualHash == report.expectedHash;
                report.budgetReservationPeakBytes = memory.Peak;
            }
            finally
            {
                ProfilerDriver.enabled = false; Profiler.enabled = false;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, priorCpu); ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, priorMemory);
                ProfilerDriver.profileEditor = priorEditor; Profiler.enabled = priorRuntimeEnabled; ProfilerDriver.enabled = priorEnabled;
                CombatEvidence.Sink = null;
                foreach (var replicator in replicators)
                {
                    replicator.Dispose(); if (!replicator.WaitForClose(10000)) report.failure = "ReplicationWorkerDidNotClose";
                    if (replicator.LastFailure != null) report.replicationFailures.Add(replicator.LastFailure);
                }
                foreach (var store in stores)
                {
                    store.Dispose(); if (!store.WaitForClose(10000)) report.failure = "StorageWorkerDidNotClose";
                    report.diagnosticDropped += store.Dropped;
                    if (store.LastFailure != null) report.writerFailures.Add(store.LastFailure);
                }
                if (report.diagnosticDropped != 0 || report.writerFailures.Count != 0 || report.replicationFailures.Count != 0 || report.failure != null)
                {
                    report.allocationMeasurementAvailable = false; report.scopedMainThreadAllocatedBytes = null;
                    report.singleCheckpointMainThreadAllocatedBytes = null;
                }
                CombatEvidence.Sink = priorSink;
            }
        }

        private static Type Nested(Type owner, string name) => owner.GetNestedType(name, BindingFlags.NonPublic) ?? throw new MissingMemberException(owner.FullName, name);
        private static long Dropped(List<CombatEvidenceStore> stores) { long result = 0; foreach (var store in stores) result += store.Dropped; return result; }
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
        private sealed class CaseReport
        {
            public int schemaVersion = 1, controllers, targetHz = Frequency, warmupSimulationSteps = WarmupSteps,
                firstMeasuredSimulationStep = WarmupSteps, measuredSimulationSteps = MeasuredSteps, stepsPerUnityFrame = StepsPerUnityFrame;
            public string mode, directory, failure, expectedHash, actualHash;
            public bool allocationMeasurementAvailable, businessMatches;
            public long? scopedMainThreadAllocatedBytes, singleCheckpointMainThreadAllocatedBytes;
            public long dotTicksInWindow, initialAndWarmupDropped, measuredWindowDropped, diagnosticDropped, budgetReservationPeakBytes;
            public double measuredWindowWallSeconds;
            public List<string> writerFailures = new(), replicationFailures = new();
            public RawProbe.Report raw;
            public string workloadSource = "Existing CombatEvidenceLoadBenchmarks.Workload/ProbeSink/Hub/Endpoint, bound by reflection outside the measured scopes";
            public string scope = "Short allocation window: 144 simulated status/Gateway/Replica steps after 144 warmup steps; includes Advance and DOT callbacks. " +
                "Measures Main Thread Step plus synthetic two-endpoint replication Tick, with the existing 144Hz Stopwatch pacing outside scope and 24 steps per Unity frame. " +
                "Unity frame grouping is not a claim of 144 rendered FPS. One checkpoint has a separate allocation scope and is excluded from step bytes. " +
                "Excludes setup, warmup, pacing, reflection field assignment, digest, worker threads and drain. Does not measure 180-second total allocations, " +
                "normal frame timing, actual Steam transport, replication catch-up, or retained diagnostic memory. Whole-frame raw totals include harness/editor allocations.";
        }
    }
}
#endif
