#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using NUnit.Framework;
using Unity.Profiling;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class CombatRawAllocationMeasurementPlayModeTests
    {
        private const int PayloadBytes = 32 << 10;

        [UnityTest]
        [Category("CombatEvidenceAllocationCalibration")]
        public IEnumerator RawMainThreadSamplesContainAllocationByteMetadata()
        {
            if (!Application.isBatchMode || Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_RAW_ALLOCATION_PROBE") != "1")
                Assert.Ignore("Run only in an isolated batch-mode Unity with COMBAT_EVIDENCE_RAW_ALLOCATION_PROBE=1; the probe clears that process's profiler history.");
            string root = Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_ALLOCATION_PROBE_OUTPUT") ??
                Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/CombatEvidenceNextValidation/allocation-probe"));
            Directory.CreateDirectory(root);
            string stem = Path.Combine(root, "raw-calibration-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"));
            string prefix = "CombatEvidence.AllocationCalibration." + Guid.NewGuid().ToString("N");
            var emptyMarker = new ProfilerMarker(prefix + ".Empty");
            var smallMarker = new ProfilerMarker(prefix + ".32KiB");
            var largeMarker = new ProfilerMarker(prefix + ".64KiB");
            var report = new Report { unityVersion = Application.unityVersion, rawProfilePath = stem + ".raw",
                scopes = new[] { new Scope { marker = prefix + ".Empty" }, new Scope { marker = prefix + ".32KiB", payloadBytes = PayloadBytes },
                    new Scope { marker = prefix + ".64KiB", payloadBytes = PayloadBytes * 2 } } };
            bool previousEnabled = ProfilerDriver.enabled, previousEditor = ProfilerDriver.profileEditor;
            bool previousCpu = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU), previousMemory = ProfilerDriver.IsAreaEnabled(ProfilerArea.Memory);
            bool previousRuntimeEnabled = Profiler.enabled;
            try
            {
                Allocate(PayloadBytes); Allocate(PayloadBytes * 2);
                ProfilerDriver.enabled = false; Profiler.enabled = false;
                ProfilerDriver.ClearAllFrames();
                ProfilerDriver.profileEditor = false;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true); ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, true);
                Profiler.enabled = true; ProfilerDriver.enabled = true;
                yield return null;
                using (emptyMarker.Auto()) Allocate(0);
                using (smallMarker.Auto()) Allocate(PayloadBytes);
                using (largeMarker.Auto()) Allocate(PayloadBytes * 2);
                // Let the sampled frame complete and reach the Editor's Profiler data store.
                for (int i = 0; i < 3; i++) yield return null;
                ProfilerDriver.enabled = false; Profiler.enabled = false;
                report.firstFrame = ProfilerDriver.firstFrameIndex; report.lastFrame = ProfilerDriver.lastFrameIndex;
                report.frameCount = report.firstFrame < 0 ? 0 : report.lastFrame - report.firstFrame + 1;
                if (report.frameCount > 0 && report.frameCount <= 10)
                {
                    report.rawProfileSaved = ProfilerDriver.SaveProfile(report.rawProfilePath) && File.Exists(report.rawProfilePath) && new FileInfo(report.rawProfilePath).Length > 0;
                    try { ReadFrames(report); } catch (Exception error) { report.failure = error.ToString(); }
                }
                else report.failure = "RawProfilerFrameCountOutside1To10:" + report.frameCount;
            }
            finally
            {
                ProfilerDriver.enabled = false; Profiler.enabled = false;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, previousCpu); ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, previousMemory);
                ProfilerDriver.profileEditor = previousEditor;
                Profiler.enabled = previousRuntimeEnabled; ProfilerDriver.enabled = previousEnabled;
            }
            File.WriteAllText(stem + ".json", EvidenceJson.Encode(report));
            TestContext.WriteLine(stem + ".json");
            if (!report.allocationBytesVerified)
                Assert.Inconclusive("Raw GC.Alloc byte metadata was not calibrated; inspect " + stem + ".json");
            Assert.That(report.rawProfileSaved, Is.True, "The calibration must retain its raw Profiler evidence.");
            Assert.That(report.failure, Is.Null);
        }

        internal static void ReadFrames(Report report)
        {
            for (int frame = report.firstFrame; frame <= report.lastFrame; frame++)
            for (int thread = 0; ; thread++)
            {
                using var data = ProfilerDriver.GetRawFrameDataView(frame, thread);
                if (!data.valid) break;
                if (data.threadName != "Main Thread") continue;
                var row = new Frame { frame = frame, threadIndex = thread, threadId = data.threadId.ToString(),
                    threadName = data.threadName, threadGroup = data.threadGroupName, sampleCount = data.sampleCount };
                report.frames.Add(row);
                int allocationMarker = data.GetMarkerId("GC.Alloc");
                if (allocationMarker == FrameDataView.invalidMarkerId) { row.missingAllocationMarker = true; continue; }
                var metadata = data.GetMarkerMetadataInfo(allocationMarker);
                foreach (var item in metadata) row.metadata.Add(new Metadata { name = item.name, type = item.type.ToString(), unit = item.unit.ToString() });
                var starts = new int[report.scopes.Length]; var ends = new int[report.scopes.Length];
                for (int scope = 0; scope < starts.Length; scope++) { starts[scope] = -1; ends[scope] = -1; }
                for (int sample = 0; sample < data.sampleCount; sample++)
                {
                    int marker = data.GetSampleMarkerId(sample);
                    for (int scope = 0; scope < report.scopes.Length; scope++)
                    {
                        if (marker != data.GetMarkerId(report.scopes[scope].marker)) continue;
                        report.scopes[scope].occurrences++;
                        starts[scope] = sample; ends[scope] = sample + data.GetSampleChildrenCountRecursive(sample);
                    }
                    if (marker != allocationMarker) continue;
                    // Unity's RawFrameDataView API specifies GC.Alloc metadata[0] as the allocated byte size.
                    // This is separate from the marker's duration returned by ProfilerRecorder.Value.
                    int count = data.GetSampleMetadataCount(sample);
                    long? bytes = count == 0 ? (long?)null : data.GetSampleMetadataAsLong(sample, 0);
                    if (bytes < 0) bytes = null;
                    var allocation = new Allocation { sample = sample, metadataCount = count, bytes = bytes };
                    row.allocations.Add(allocation);
                    if (bytes.HasValue) row.observedAllocationBytes += bytes.Value; else row.missingByteMetadata++;
                    for (int scope = 0; scope < report.scopes.Length; scope++)
                    {
                        if (sample <= starts[scope] || sample > ends[scope]) continue;
                        allocation.scope = report.scopes[scope].marker;
                        var target = report.scopes[scope]; target.allocationEvents++;
                        if (bytes.HasValue) target.observedBytes += bytes.Value; else target.missingByteMetadata++;
                    }
                }
            }
            var empty = report.scopes[0]; var small = report.scopes[1]; var large = report.scopes[2];
            report.allocationBytesVerified = report.failure == null && report.rawProfileSaved &&
                empty.occurrences == 1 && small.occurrences == 1 && large.occurrences == 1 &&
                empty.observedBytes == 0 && empty.missingByteMetadata == 0 && small.missingByteMetadata == 0 && large.missingByteMetadata == 0 &&
                small.observedBytes >= PayloadBytes && large.observedBytes >= PayloadBytes * 2 &&
                large.observedBytes - small.observedBytes == PayloadBytes;
            if (report.allocationBytesVerified)
            {
                report.calibrated32KiBAllocationBytes = small.observedBytes;
                report.calibrated64KiBAllocationBytes = large.observedBytes;
                foreach (var row in report.frames)
                    if (!row.missingAllocationMarker && row.missingByteMetadata == 0) row.mainThreadFrameAllocationBytes = row.observedAllocationBytes;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Allocate(int bytes)
        {
            if (bytes == 0) return;
            var buffer = new byte[bytes]; buffer[0] = 1; GC.KeepAlive(buffer);
        }

        internal sealed class Allocation { public int sample, metadataCount; public long? bytes; public string scope; }
        internal sealed class Metadata { public string name, type, unit; }
        internal sealed class Scope
        {
            public string marker; public int payloadBytes, occurrences, allocationEvents, missingByteMetadata; public long observedBytes;
        }
        internal sealed class Frame
        {
            public int frame, threadIndex, sampleCount, missingByteMetadata;
            public string threadId, threadName, threadGroup;
            public bool missingAllocationMarker;
            public long observedAllocationBytes; public long? mainThreadFrameAllocationBytes;
            public List<Metadata> metadata = new(); public List<Allocation> allocations = new();
        }
        internal sealed class Report
        {
            public int schemaVersion = 1, firstFrame, lastFrame, frameCount;
            public string unityVersion, rawProfilePath, failure;
            public bool rawProfileSaved, allocationBytesVerified;
            public long? calibrated32KiBAllocationBytes, calibrated64KiBAllocationBytes;
            public Scope[] scopes; public List<Frame> frames = new();
            public string scope = "Unity Editor PlayMode, raw GC.Alloc metadata[0] from Main Thread only. Scoped calibration bytes exclude surrounding " +
                "test/editor allocations; frame totals include them. This does not measure writer/worker allocations or retained diagnostic memory. " +
                "Profiler overhead affects these frames; do not use these timings as normal workload performance. Raw values remain observations until calibration passes.";
        }
    }
}
#endif
