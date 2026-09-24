#if UNITY_EDITOR
using System;
using System.Reflection;
using NUnit.Framework;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class CombatArchivedTailAlgorithmsTests
    {
        private static object Invoke(string method, params object[] arguments)
        {
            var owner = typeof(CombatArchivedTailAlgorithmsTests).Assembly.GetType(
                "MonsterSupergroup.Gameplay.Tests.CombatArchivedTailAlgorithms");
            Assert.That(owner, Is.Not.Null, "Archived Step audit algorithms must exist before archived data can be accepted.");
            var target = owner.GetMethod(method, BindingFlags.Static | BindingFlags.Public);
            Assert.That(target, Is.Not.Null, method);
            return target.Invoke(null, arguments);
        }

        [Test]
        public void MissingAllocationMetadataOrCalibrationNeverBecomesZero()
        {
            Assert.That(Invoke("AllocationBytes", 23L, 0, true, true), Is.EqualTo(23L));
            Assert.That(Invoke("AllocationBytes", 0L, 1, true, true), Is.Null);
            Assert.That(Invoke("AllocationBytes", 23L, 0, false, true), Is.Null);
            Assert.That(Invoke("AllocationBytes", 23L, 0, true, false), Is.Null);
        }

        [Test]
        public void GcOverlapClipsUnionsAndExcludesCalibration()
        {
            Assert.That(Invoke("OverlapUnionNs", 10UL, 30UL,
                new ulong[] { 5, 15, 29, 10 }, new ulong[] { 20, 25, 40, 30 },
                new[] { false, false, false, true }), Is.EqualTo(16UL));
            Assert.That(Invoke("OverlapUnionNs", 10UL, 30UL,
                new ulong[] { 1, 30 }, new ulong[] { 10, 40 }, new[] { false, false }), Is.EqualTo(0UL));
        }

        [Test]
        public void MissingGcCoverageRemainsUnknownDespiteNoObservedOverlap()
        {
            Assert.That(Invoke("GcCoverageReason", true, true), Is.Null);
            Assert.That(Invoke("GcCoverageReason", true, false), Is.EqualTo("SourceGcIntervalCoverageUnverified"));
            Assert.That(Invoke("GcCoverageReason", false, true), Is.EqualTo("RequiredTargetThreadCoverageMissing"));
        }

        [Test]
        public void StepIntervalsRequireCountOrderAndLoadContainment()
        {
            Assert.That(Invoke("ValidSteps", new ulong[] { 10, 20 }, new ulong[] { 15, 25 }, 10UL, 30UL, 2), Is.True);
            Assert.That(Invoke("ValidSteps", new ulong[] { 10, 14 }, new ulong[] { 15, 25 }, 10UL, 30UL, 2), Is.False);
            Assert.That(Invoke("ValidSteps", new ulong[] { 10, 20 }, new ulong[] { 15, 35 }, 10UL, 30UL, 2), Is.False);
            Assert.That(Invoke("ValidSteps", new ulong[] { 10 }, new ulong[] { 15 }, 10UL, 30UL, 2), Is.False);
        }

        [Test]
        public void PrefixMustComeFromRegisteredMainWorkMarker()
        {
            Assert.That(Invoke("Prefix", "CombatEvidence.FullAllocation.abc.main.Work"), Is.EqualTo("CombatEvidence.FullAllocation.abc"));
            Assert.That(Invoke("Prefix", "other.main.Work"), Is.Null);
            Assert.That(Invoke("Prefix", "CombatEvidence.FullAllocation.abc.writer.Work"), Is.Null);
        }
    }
}
#endif
#if UNITY_EDITOR
namespace MonsterSupergroup.Gameplay.Tests
{
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using MonsterSupergroup.NetworkCombat.Diagnostics;
    using UnityEditor.Profiling;
    using UnityEditorInternal;
    using UnityEngine;
    using UnityEngine.Profiling;

    internal static class CombatArchivedTailAlgorithms
    {
        public static long? AllocationBytes(long known, int missing, bool calibrated, bool valid)
            => known >= 0 && missing == 0 && calibrated && valid ? known : (long?)null;
        public static string GcCoverageReason(bool targetCoverage, bool sourceCoverage)
            => !targetCoverage ? "RequiredTargetThreadCoverageMissing" : !sourceCoverage ? "SourceGcIntervalCoverageUnverified" : null;
        public static string Prefix(string marker)
        {
            const string suffix = ".main.Work", start = "CombatEvidence.FullAllocation.";
            return marker != null && marker.StartsWith(start, StringComparison.Ordinal) && marker.EndsWith(suffix, StringComparison.Ordinal)
                && marker.Length > start.Length + suffix.Length ? marker.Substring(0, marker.Length - suffix.Length) : null;
        }
        public static bool ValidSteps(ulong[] starts, ulong[] ends, ulong begin, ulong end, int expected)
        {
            if (starts.Length != expected || ends.Length != expected || begin >= end) return false;
            for (int i = 0; i < starts.Length; i++)
                if (starts[i] < begin || ends[i] > end || ends[i] <= starts[i] || (i > 0 && starts[i] < ends[i - 1])) return false;
            return true;
        }
        public static ulong OverlapUnionNs(ulong begin, ulong end, ulong[] starts, ulong[] ends, bool[] calibration)
        {
            if (starts.Length != ends.Length || starts.Length != calibration.Length) throw new ArgumentException("Span lengths differ.");
            var intervals = new List<(ulong begin, ulong end)>(starts.Length);
            for (int i = 0; i < starts.Length; i++)
                if (!calibration[i] && starts[i] < end && ends[i] > begin)
                    intervals.Add((Math.Max(begin, starts[i]), Math.Min(end, ends[i])));
            intervals.Sort((a, b) => a.begin.CompareTo(b.begin));
            if (intervals.Count == 0) return 0;
            ulong left = intervals[0].begin, right = intervals[0].end, total = 0;
            foreach (var item in intervals.Skip(1))
                if (item.begin <= right) right = Math.Max(right, item.end);
                else { total += right - left; left = item.begin; right = item.end; }
            return total + right - left;
        }
    }

    public sealed class CombatArchivedTailAuditTests
    {
        private const int MaximumFrames = 64, MaximumRawThreads = 128, MaximumSamples = 1048576, MaximumGcSpans = 2048, ExpectedSteps = 720;

        [Test, Timeout(14400000), Category("CombatEvidenceArchivedTailAudit")]
        public void ArchivedProfilesExportEveryMeasuredStepWithoutRecapture()
        {
            string manifestPath = Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_ARCHIVED_TAIL_MANIFEST");
            string output = Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_ARCHIVED_TAIL_OUTPUT");
            if (string.IsNullOrEmpty(manifestPath) || string.IsNullOrEmpty(output))
                Assert.Ignore("Requires explicit archived-profile manifest and a new independent output directory.");
            manifestPath = Path.GetFullPath(manifestPath); output = Path.GetFullPath(output);
            Assert.That(Application.isBatchMode, Is.True);
            Assert.That(ProfilerDriver.enabled || Profiler.enabled, Is.False, "Offline audit must never enable capture.");
            Assert.That(Directory.Exists(output) || File.Exists(output), Is.False, "Never overwrite a previous analysis.");
            string manifestBefore = Hash(manifestPath);
            var manifest = EvidenceJson.Decode<Manifest>(File.ReadAllText(manifestPath));
            Assert.That(manifest.schemaVersion, Is.EqualTo(1)); Assert.That(manifest.expectedCases, Is.EqualTo(27));
            Assert.That(manifest.cases.Length, Is.EqualTo(27));
            string archiveRoot = Path.GetFullPath(manifest.archiveRoot);
            Assert.That(IsWithin(output, archiveRoot), Is.False, "The complete sealed archive must stay read-only.");
            var keys = new HashSet<string>();
            foreach (var item in manifest.cases)
            {
                Assert.That(new[] { 50, 200, 500 }, Does.Contain(item.controllers));
                Assert.That(new[] { "off", "local", "replicated" }, Does.Contain(item.mode));
                Assert.That(item.repeat, Is.InRange(1, 3));
                Assert.That(keys.Add(Key(item.controllers, item.mode, item.repeat)), Is.True);
                Assert.That(IsWithin(Path.GetFullPath(item.allocationJson), archiveRoot), Is.True);
                Assert.That(IsWithin(output, Path.GetDirectoryName(Path.GetFullPath(item.allocationJson))), Is.False);
            }
            Directory.CreateDirectory(output);
            var reports = new List<AuditReport>(27);
            foreach (var entry in manifest.cases)
            {
                var report = new AuditReport { allocationJson = Path.GetFullPath(entry.allocationJson),
                    controllers = entry.controllers, mode = entry.mode, repeat = entry.repeat };
                reports.Add(report);
                try
                {
                    report.allocationBefore = Hash(report.allocationJson);
                    Require(report.allocationBefore == entry.allocationSha256, "AllocationManifestHashMismatch");
                    var source = EvidenceJson.Decode<SourceReport>(File.ReadAllText(report.allocationJson));
                    Require(source.controllers == entry.controllers && source.mode == entry.mode && source.repeat == entry.repeat, "CaseIdentityMismatch");
                    report.rawProfile = Path.GetFullPath(source.rawProfilePath);
                    Require(IsWithin(report.rawProfile, archiveRoot), "ProfileOutsideExplicitArchive");
                    Require(!IsWithin(output, Path.GetDirectoryName(report.rawProfile)), "OutputWouldModifyArchivedProfileDirectory");
                    report.rawBefore = Hash(report.rawProfile);
                    Require(report.rawBefore == entry.rawSha256, "RawManifestHashMismatch");
                    ReadCase(source, report);
                }
                catch (Exception error) { report.errors.Add(error.ToString()); }
                finally
                {
                    ProfilerDriver.ClearAllFrames();
                    try
                    {
                        report.allocationAfter = Hash(report.allocationJson);
                        if (report.rawProfile != null) report.rawAfter = Hash(report.rawProfile);
                        report.inputsUnchanged = report.allocationBefore == report.allocationAfter &&
                            report.rawBefore != null && report.rawBefore == report.rawAfter;
                        if (!report.inputsUnchanged) report.errors.Add("InputChangedDuringOfflineAudit");
                    }
                    catch (Exception error) { report.errors.Add("AfterHash:" + error); }
                    report.auditPassed = report.errors.Count == 0 && report.inputsUnchanged && report.steps.Count == ExpectedSteps;
                    string stem = Key(report.controllers, report.mode, report.repeat);
                    File.WriteAllText(Path.Combine(output, stem + "-steps.json"), EvidenceJson.Encode(report));
                    WriteCsv(Path.Combine(output, stem + "-steps.csv"), report.steps);
                }
            }
            string manifestAfter = Hash(manifestPath);
            File.WriteAllText(Path.Combine(output, "tail-audit.json"), EvidenceJson.Encode(new {
                schemaVersion = 1, manifestPath, manifestBefore, manifestAfter, manifestUnchanged = manifestBefore == manifestAfter,
                cases = reports.Select(r => new { r.controllers, r.mode, r.repeat, r.auditPassed, steps = r.steps.Count,
                    r.inputsUnchanged, r.allocationAvailable, r.gcCoverageAvailable, r.errors }).ToArray(),
                scope = "Offline main Step details from archived raw data only; no recapture, deep profiling or call stacks. " +
                    "GC overlap is the union of known markers on registered threads, not a process-wide pause or causal attribution. " +
                    "Unavailable source GC coverage remains unknown even when observed overlap is zero. Other runs are never time-aligned." }));
            Assert.That(manifestAfter, Is.EqualTo(manifestBefore));
            Assert.That(reports.All(r => r.auditPassed), Is.True, string.Join("\n", reports.Where(r => !r.auditPassed)
                .Select(r => Key(r.controllers, r.mode, r.repeat) + ": " + string.Join("; ", r.errors))));
        }

        private static void ReadCase(SourceReport source, AuditReport report)
        {
            var targets = source.threads?.Where(t => t != null).ToArray();
            Require(targets != null && targets.Length == source.expectedThreads && (targets.Length == 1 || targets.Length == 2 || targets.Length == 5), "InvalidRegisteredThreads");
            var main = targets.Single(t => t.role == "main");
            string prefix = CombatArchivedTailAlgorithms.Prefix(main.workMarker);
            Require(prefix != null, "UnrecognizedMainMarkerPrefix");
            report.prefix = prefix; report.mainThreadId = main.threadId;
            report.sourceGcMeasurementAvailable = source.gcMeasurementAvailable;
            Require(targets.Select(t => t.threadId).Distinct().Count() == targets.Length, "DuplicateRegisteredThreadIdentity");
            ProfilerDriver.ClearAllFrames();
            Require(ProfilerDriver.LoadProfile(report.rawProfile, false), "RawProfileDidNotLoad");
            report.firstFrame = ProfilerDriver.firstFrameIndex; report.lastFrame = ProfilerDriver.lastFrameIndex;
            Require(report.firstFrame >= 0 && report.lastFrame >= report.firstFrame && report.lastFrame - report.firstFrame + 1 <= MaximumFrames, "RawFrameBoundExceeded");
            ulong begin = 0, end = 0;
            int beginCount = 0, endCount = 0;
            using (var iterator = new ProfilerFrameDataIterator())
                for (int frame = report.firstFrame; frame <= report.lastFrame; frame++)
                    for (int index = 0; index < Math.Min(iterator.GetThreadCount(frame), MaximumRawThreads); index++)
                    {
                        using var data = ProfilerDriver.GetRawFrameDataView(frame, index);
                        if (!data.valid) break;
                        if (data.threadName != main.threadName || data.threadId.ToString() != main.threadId) continue;
                        Require(data.sampleCount < MaximumSamples, "AnchorSampleCapacityReached");
                        int first = data.GetMarkerId(prefix + ".LoadStart"), last = data.GetMarkerId(prefix + ".LoadEnd");
                        for (int sample = 0; sample < data.sampleCount; sample++)
                        {
                            int marker = data.GetSampleMarkerId(sample);
                            if (first != FrameDataView.invalidMarkerId && marker == first) { beginCount++; begin = data.GetSampleStartTimeNs(sample); }
                            if (last != FrameDataView.invalidMarkerId && marker == last) { endCount++; end = data.GetSampleStartTimeNs(sample); }
                        }
                        break;
                    }
            Require(beginCount == 1 && endCount == 1 && begin < end, "LoadAnchorsMissingOrDuplicated");
            Require(begin == source.rawLoadStartNs && end == source.rawLoadEndNs, "ArchivedAnchorMismatch");
            report.loadBeginNs = begin; report.loadEndNs = end;
            bool coverage = true;
            using (var iterator = new ProfilerFrameDataIterator())
                for (int frame = report.firstFrame; frame <= report.lastFrame; frame++)
                {
                    var scan = new Scan { frame = frame, totalRawThreads = iterator.GetThreadCount(frame) };
                    report.scans.Add(scan);
                    var found = new bool[targets.Length];
                    bool intersects = false, mainSeen = false;
                    for (int index = 0; index < Math.Min(scan.totalRawThreads, MaximumRawThreads); index++)
                    {
                        scan.scannedThreads++;
                        using var data = ProfilerDriver.GetRawFrameDataView(frame, index);
                        if (!data.valid) { scan.invalidView = true; break; }
                        int target = Array.FindIndex(targets, t => t.threadName == data.threadName);
                        if (target < 0) continue;
                        Require(data.threadId.ToString() == targets[target].threadId, "TargetThreadIdentityChanged");
                        Require(!found[target], "DuplicateTargetThreadView");
                        found[target] = true; scan.matchedTargets++;
                        Require(data.sampleCount < MaximumSamples, "TargetSampleCapacityReached");
                        if (targets[target].role == "main")
                        {
                            mainSeen = true;
                            intersects = data.frameStartTimeNs < end && data.frameStartTimeNs + data.frameTimeNs > begin;
                        }
                        ReadSamples(data, targets[target], prefix, frame, begin, end, report);
                        if (scan.matchedTargets == targets.Length) break;
                    }
                    scan.intersectsLoad = intersects; scan.mainSeen = mainSeen;
                    scan.scanTruncated = scan.scannedThreads == MaximumRawThreads && scan.matchedTargets != targets.Length && scan.totalRawThreads > MaximumRawThreads;
                    scan.requiredTargetsComplete = mainSeen && (!intersects || scan.matchedTargets == targets.Length);
                    if (!scan.requiredTargetsComplete) coverage = false;
                }
            Require(coverage, "RequiredTargetThreadCoverageMissing");
            report.steps.Sort((a, b) => a.beginNs.CompareTo(b.beginNs));
            Require(CombatArchivedTailAlgorithms.ValidSteps(report.steps.Select(s => s.beginNs).ToArray(), report.steps.Select(s => s.endNs).ToArray(), begin, end, ExpectedSteps), "StepCountOrderOrBoundsInvalid");
            report.requiredTargetCoverage = coverage;
            report.gcCoverageAvailable = coverage && source.gcMeasurementAvailable && !source.gcSpanCapacityReached;
            bool validAllocation = source.allocationMeasurementAvailable && source.rawProfileSaved && !source.sampleCapacityReached && source.profilerOverflowLogs == 0;
            report.allocationAvailable = validAllocation && main.calibrated;
            var starts = report.gcSpans.Select(s => s.beginNs).ToArray();
            var ends = report.gcSpans.Select(s => s.endNs).ToArray();
            var calibration = report.gcSpans.Select(s => s.calibration).ToArray();
            for (int i = 0; i < report.steps.Count; i++)
            {
                var step = report.steps[i]; step.step = i; step.wallNs = step.endNs - step.beginNs;
                step.allocatedBytes = CombatArchivedTailAlgorithms.AllocationBytes(step.knownAllocationBytes, step.missingByteMetadata, main.calibrated, validAllocation);
                step.allocationReason = step.allocatedBytes.HasValue ? null : !main.calibrated ? "MainThreadCalibrationUnavailable" : !validAllocation ? "SourceAllocationValidityUnavailable" : "MissingAllocationByteMetadata";
                step.knownGcOverlapNs = CombatArchivedTailAlgorithms.OverlapUnionNs(step.beginNs, step.endNs, starts, ends, calibration);
                step.gcCoverageReason = CombatArchivedTailAlgorithms.GcCoverageReason(coverage, report.gcCoverageAvailable);
                step.gcOverlapNs = step.gcCoverageReason == null ? step.knownGcOverlapNs : (ulong?)null;
            }
            report.knownStepAllocatedBytes = report.steps.Sum(s => s.knownAllocationBytes);
            Require(report.knownStepAllocatedBytes == main.stepBytes, "ArchivedStepAllocationTotalMismatch");
            Require(report.steps.All(s => s.allocatedBytes.HasValue), "StepAllocationMetadataUnavailable");
            report.observedGcUnionNs = CombatArchivedTailAlgorithms.OverlapUnionNs(begin, end, starts, ends, calibration);
        }

        private static void ReadSamples(RawFrameDataView data, SourceThread thread, string prefix, int frame, ulong begin, ulong end, AuditReport report)
        {
            int allocation = data.GetMarkerId("GC.Alloc"), stepId = data.GetMarkerId(prefix + ".Step");
            Step active = null;
            int stepStart = -1, stepEnd = -1;
            for (int sample = 0; sample < data.sampleCount; sample++)
            {
                int marker = data.GetSampleMarkerId(sample);
                ulong at = data.GetSampleStartTimeNs(sample);
                bool isCalibration = thread.calibration != null && thread.calibration.beginNs < thread.calibration.endNs &&
                    at >= thread.calibration.beginNs && at < thread.calibration.endNs;
                if (thread.role == "main" && stepId != FrameDataView.invalidMarkerId && marker == stepId)
                {
                    Require(report.steps.Count < ExpectedSteps, "StepCapacityExceeded");
                    ulong duration = data.GetSampleTimeNs(sample);
                    Require(ulong.MaxValue - at >= duration, "StepTimestampOverflow");
                    Require(active == null || sample > stepEnd, "NestedStepMarkers");
                    active = new Step { unityFrame = frame, sampleIndex = sample, beginNs = at, endNs = at + duration };
                    report.steps.Add(active); stepStart = sample; stepEnd = sample + data.GetSampleChildrenCountRecursive(sample);
                    Require(stepEnd >= sample && stepEnd < data.sampleCount, "StepSampleTreeOutOfBounds");
                }
                if (allocation != FrameDataView.invalidMarkerId && marker == allocation)
                {
                    if (active == null || sample <= stepStart || sample > stepEnd) continue;
                    long? bytes = data.GetSampleMetadataCount(sample) > 0 ? data.GetSampleMetadataAsLong(sample, 0) : (long?)null;
                    if (bytes < 0) bytes = null;
                    if (isCalibration)
                    {
                        active.excludedCalibrationEvents++;
                        if (bytes.HasValue) active.excludedCalibrationBytes += bytes.Value;
                        else active.excludedCalibrationMissingMetadata++;
                        continue;
                    }
                    active.allocationEvents++;
                    if (bytes.HasValue) active.knownAllocationBytes += bytes.Value;
                    else active.missingByteMetadata++;
                    continue;
                }
                string name = data.GetSampleName(sample);
                if (name == null || (name.IndexOf("GC.Collect", StringComparison.Ordinal) < 0 && name.IndexOf("GarbageCollector.Collect", StringComparison.Ordinal) < 0)) continue;
                ulong time = data.GetSampleTimeNs(sample);
                Require(ulong.MaxValue - at >= time, "GcTimestampOverflow");
                ulong finish = at + time;
                if (at >= end || finish <= begin || finish <= at) continue;
                Require(report.gcSpans.Count < MaximumGcSpans, "GcSpanCapacityExceeded");
                report.gcSpans.Add(new GcSpan { beginNs = Math.Max(begin, at), endNs = Math.Min(end, finish),
                    threadId = thread.threadId, role = thread.role, marker = name, calibration = isCalibration });
            }
        }

        private static string Hash(string path)
        {
            using var stream = File.OpenRead(path); using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static bool IsWithin(string path, string root)
            => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        private static string Key(int controllers, string mode, int repeat) => controllers + "-" + mode + "-" + repeat;
        private static void Require(bool condition, string reason) { if (!condition) throw new InvalidDataException(reason); }
        private static void WriteCsv(string path, List<Step> steps)
        {
            using var writer = new StreamWriter(path);
            writer.WriteLine("step,unityFrame,sampleIndex,beginNs,endNs,wallNs,knownAllocationBytes,allocatedBytes,allocationEvents,missingByteMetadata,excludedCalibrationBytes,knownGcOverlapNs,gcOverlapNs,gcCoverageReason");
            foreach (var s in steps) writer.WriteLine(string.Join(",", s.step, s.unityFrame, s.sampleIndex,
                s.beginNs.ToString(CultureInfo.InvariantCulture), s.endNs.ToString(CultureInfo.InvariantCulture), s.wallNs.ToString(CultureInfo.InvariantCulture),
                s.knownAllocationBytes, s.allocatedBytes?.ToString(CultureInfo.InvariantCulture) ?? "", s.allocationEvents, s.missingByteMetadata,
                s.excludedCalibrationBytes, s.knownGcOverlapNs.ToString(CultureInfo.InvariantCulture), s.gcOverlapNs?.ToString(CultureInfo.InvariantCulture) ?? "", s.gcCoverageReason ?? ""));
        }
        private sealed class Manifest { public int schemaVersion, expectedCases; public string archiveRoot; public Entry[] cases; }
        private sealed class Entry { public string allocationJson, allocationSha256, rawSha256, mode; public int controllers, repeat; }
        private sealed class SourceReport
        {
            public int controllers, repeat, expectedThreads, profilerOverflowLogs;
            public string mode, rawProfilePath;
            public bool allocationMeasurementAvailable, gcMeasurementAvailable, rawProfileSaved, sampleCapacityReached, gcSpanCapacityReached;
            public ulong rawLoadStartNs, rawLoadEndNs;
            public SourceThread[] threads;
        }
        private sealed class SourceThread
        {
            public string role, threadName, threadId, workMarker;
            public bool calibrated;
            public long stepBytes;
            public Calibration calibration;
        }
        private sealed class Calibration { public ulong beginNs, endNs; }
        private sealed class Step
        {
            public int step, unityFrame, sampleIndex, allocationEvents, missingByteMetadata, excludedCalibrationEvents, excludedCalibrationMissingMetadata;
            public ulong beginNs, endNs, wallNs, knownGcOverlapNs;
            public ulong? gcOverlapNs;
            public long knownAllocationBytes, excludedCalibrationBytes;
            public long? allocatedBytes;
            public string allocationReason, gcCoverageReason;
        }
        private sealed class Scan
        {
            public int frame, totalRawThreads, scannedThreads, matchedTargets;
            public bool mainSeen, intersectsLoad, scanTruncated, requiredTargetsComplete, invalidView;
        }
        private sealed class GcSpan { public ulong beginNs, endNs; public string threadId, role, marker; public bool calibration; }
        private sealed class AuditReport
        {
            public int schemaVersion = 1, controllers, repeat, firstFrame, lastFrame;
            public string mode, allocationJson, rawProfile, prefix, mainThreadId, allocationBefore, allocationAfter, rawBefore, rawAfter;
            public bool inputsUnchanged, auditPassed, requiredTargetCoverage, allocationAvailable, gcCoverageAvailable, sourceGcMeasurementAvailable;
            public ulong loadBeginNs, loadEndNs, observedGcUnionNs;
            public long knownStepAllocatedBytes;
            public List<string> errors = new();
            public List<Step> steps = new(ExpectedSteps);
            public List<Scan> scans = new(MaximumFrames);
            public List<GcSpan> gcSpans = new(MaximumGcSpans);
        }
    }
}
#endif

