using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using NUnit.Framework;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    /// <summary>Calibrates a measurement API; this is not a diagnostics workload benchmark.</summary>
    public sealed class CombatAllocationMeasurementPlayModeTests
    {
        private const int ProbeBytes = 32 << 10;
        private const int Capacity = 4096;

        [UnityTest]
        [Category("CombatEvidenceAllocationCalibration")]
        public IEnumerator MainThreadAllocationRecorderObservesProbeAndExcludesWorker()
        {
            if (Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_ALLOCATION_RECORDER_PROBE") != "1")
                Assert.Ignore("Opt in to recorder calibration; raw-frame byte calibration is the supported fallback.");
            Allocate(ProbeBytes); // Warm the allocation method before recording it.
            yield return null;
            var report = new Report { unityVersion = Application.unityVersion, mainThreadId = Thread.CurrentThread.ManagedThreadId };
            try
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                Allocate(ProbeBytes);
                long after = GC.GetAllocatedBytesForCurrentThread();
                report.monoBefore = before; report.monoAfter = after; report.monoObservedDelta = after - before;
                report.monoAllocationBytesAvailable = after - before >= ProbeBytes;
            }
            catch (Exception error) { report.monoFailure = error.ToString(); }

            try { Calibrate(report); }
            catch (Exception error) { report.failure = error.ToString(); }
            string root = Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_ALLOCATION_PROBE_OUTPUT") ??
                Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/CombatEvidenceNextValidation/allocation-probe"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "calibration-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".json");
            File.WriteAllText(path, EvidenceJson.Encode(report));
            TestContext.WriteLine(path);
            if (!report.mainThreadAllocationEventsVerified)
                Assert.Inconclusive("GC.Alloc allocation events or thread filtering are unverified. See " + path);
            if (!report.mainThreadAllocationBytesVerified)
                Assert.Inconclusive("GC.Alloc events are verified, but byte measurement is unverified. See " + path);
            Assert.That(report.failure, Is.Null, path);
        }

        private static void Calibrate(Report report)
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            ProfilerRecorderHandle selected = default;
            foreach (var handle in handles)
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                if (description.Name != "GC.Alloc") continue;
                selected = handle;
                report.marker = description.Name; report.category = description.Category.Name;
                report.unit = description.UnitType.ToString(); report.dataType = description.DataType.ToString();
                break;
            }
            if (!selected.Valid) { report.reason = "GC.Alloc marker unavailable"; return; }

            // No frame summing and no wrapping: each event is recorded immediately; saturation is rejected below.
            using var main = new ProfilerRecorder(selected, Capacity, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            using var all = new ProfilerRecorder(selected, Capacity, ProfilerRecorderOptions.None);
            if (!main.Valid || !all.Valid) { report.reason = "ProfilerRecorder invalid"; return; }
            using var worker = new AllocationWorker();
            worker.Run(0); // Warm cross-thread signalling before the measured interval.
            Capture(main, 0); Capture(main, ProbeBytes); // Warm recorder accessors and native attachment.
            report.empty = Capture(main, 0);
            report.probe32KiB = Capture(main, ProbeBytes);
            report.probe64KiB = Capture(main, ProbeBytes * 2);

            main.Reset(); all.Reset(); main.Start(); all.Start();
            worker.Run(ProbeBytes);
            all.Stop(); main.Stop();
            report.workerObservedOnMain = Read(main);
            report.workerObservedOnAllThreads = Read(all);
            report.workerThreadId = worker.ThreadId;
            report.threadFilterVerified = worker.ThreadId != report.mainThreadId &&
                report.workerObservedOnMain.events == 0 && report.workerObservedOnAllThreads.events > 0 &&
                !report.workerObservedOnMain.saturated && !report.workerObservedOnAllThreads.saturated;
            report.mainThreadAllocationEventsVerified = report.empty.events == 0 &&
                report.probe32KiB.events > 0 && report.probe64KiB.events > 0 &&
                !report.probe32KiB.saturated && !report.probe64KiB.saturated && report.threadFilterVerified;

            // A GC.Alloc sample can be a timing marker. Its duration/count cannot stand in for allocated bytes.
            report.mainThreadAllocationBytesVerified = report.mainThreadAllocationEventsVerified &&
                main.UnitType == ProfilerMarkerDataUnit.Bytes && report.probe32KiB.value >= ProbeBytes &&
                report.probe64KiB.value >= ProbeBytes * 2 &&
                report.probe64KiB.value - report.probe32KiB.value == ProbeBytes;
            if (report.mainThreadAllocationBytesVerified)
            {
                report.observed32KiBAllocationBytes = report.probe32KiB.value;
                report.observed64KiBAllocationBytes = report.probe64KiB.value;
            }
            report.reason = report.mainThreadAllocationBytesVerified ? "Byte size and thread isolation calibrated" :
                report.mainThreadAllocationEventsVerified ? "Allocation events observed; byte unit or size calibration unavailable" :
                "Allocation event or thread isolation calibration failed";
        }

        private static Samples Capture(ProfilerRecorder recorder, int bytes)
        {
            recorder.Reset(); recorder.Start();
            Allocate(bytes);
            recorder.Stop();
            return Read(recorder);
        }

        private static Samples Read(ProfilerRecorder recorder)
        {
            var samples = new Samples { count = recorder.Count, saturated = recorder.WrappedAround || recorder.Count >= Capacity };
            for (int i = 0; i < samples.count; i++)
            {
                var sample = recorder.GetSample(i);
                samples.events += sample.Count; samples.value += sample.Value;
            }
            return samples;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Allocate(int bytes)
        {
            if (bytes == 0) return;
            var buffer = new byte[bytes]; buffer[0] = 1; GC.KeepAlive(buffer);
        }

        private sealed class AllocationWorker : IDisposable
        {
            private readonly AutoResetEvent request = new(false), completed = new(false);
            private readonly Thread thread;
            private int bytes;
            private Exception failure;
            public int ThreadId { get; private set; }
            public AllocationWorker()
            {
                thread = new Thread(Work) { IsBackground = true, Name = "Combat allocation calibration" };
                thread.Start();
                if (!completed.WaitOne(10000)) throw new TimeoutException("Allocation worker did not initialize.");
                if (failure != null) throw new InvalidOperationException("Allocation worker initialization failed.", failure);
            }
            private void Work()
            {
                try
                {
                    UnityEngine.Profiling.Profiler.BeginThreadProfiling("CombatEvidenceCalibration", "Allocation worker");
                    ThreadId = Thread.CurrentThread.ManagedThreadId;
                    Allocate(ProbeBytes); completed.Set();
                    while (request.WaitOne())
                    {
                        int requested = Volatile.Read(ref bytes);
                        if (requested < 0) break;
                        Allocate(requested); completed.Set();
                    }
                }
                catch (Exception error) { failure = error; completed.Set(); }
                finally { UnityEngine.Profiling.Profiler.EndThreadProfiling(); }
            }
            public void Run(int allocationBytes)
            {
                Volatile.Write(ref bytes, allocationBytes); request.Set();
                if (!completed.WaitOne(10000)) throw new TimeoutException("Allocation worker did not finish.");
                if (failure != null) throw new InvalidOperationException("Allocation worker failed.", failure);
            }
            public void Dispose()
            {
                Volatile.Write(ref bytes, -1); request.Set();
                if (!thread.Join(10000)) throw new TimeoutException("Allocation worker did not stop.");
                request.Dispose(); completed.Dispose();
            }
        }

        private sealed class Samples { public int count; public long events, value; public bool saturated; }
        private sealed class Report
        {
            public int schemaVersion = 1;
            public string unityVersion, marker, category, unit, dataType, reason, failure, monoFailure;
            public int mainThreadId, workerThreadId, allocatedProbePayloadBytes = ProbeBytes;
            public long? monoBefore, monoAfter, monoObservedDelta, observed32KiBAllocationBytes, observed64KiBAllocationBytes;
            public bool monoAllocationBytesAvailable, threadFilterVerified, mainThreadAllocationEventsVerified, mainThreadAllocationBytesVerified;
            public Samples empty, probe32KiB, probe64KiB, workerObservedOnMain, workerObservedOnAllThreads;
            public string scope = "Synchronous allocation method on the initializing Unity test main thread; worker allocation is a control. " +
                "This calibration does not measure the diagnostic workload, writers, frame allocations, retained heap, or native memory. " +
                "Sample value is in the reported unit. Unverified allocation byte results remain null.";
        }
    }
}
