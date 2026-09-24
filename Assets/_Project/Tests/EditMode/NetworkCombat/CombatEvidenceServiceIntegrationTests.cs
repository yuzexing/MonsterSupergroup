using System;
using System.IO;
using System.Linq;
using System.Threading;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatEvidenceServiceIntegrationTests
    {
        [Test] public void RecordIdentityAndCheckpointPersistenceStagesKeepCleanupInsideService()
        {
            string directory = TemporaryDirectory();
            var memory = new DiagnosticMemoryBudget();
            try
            {
                using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { ObserveQueue = true, Memory = memory });
                Assert.That(store.TryWrite(Record(1, "replay.checkpoint", new { state = new string('x', 5000) })), Is.True);
                Close(store);
                Assert.That(memory.Used, Is.EqualTo(EvidenceQueueObservation.ReservedBytes), "All queued and codec ownership must already be released before export.");
                var report = Export(store, directory);
                Assert.That(memory.Used, Is.Zero);
                var work = WorkItems(report).Single(item => (string)item["kind"] == "Record");
                Assert.That((ulong)work["firstSequence"], Is.EqualTo(1));
                Assert.That((ulong)work["lastSequence"], Is.EqualTo(1));
                Assert.That((int)work["logicalCount"], Is.EqualTo(1));
                Assert.That((string)work["stage"], Is.EqualTo("replay.checkpoint"));
                Assert.That((string)work["source"]["runId"], Is.EqualTo("run"));
                Assert.That((string)work["source"]["captureId"], Is.EqualTo("capture"));
                Assert.That((bool)work["identityComplete"], Is.True);
                foreach (string stage in new[] { "Append", "SourceOpen", "DirectoryCreate", "SourceRecover", "Manifest", "Rotation", "EventOpen", "InputJson",
                    "Payload", "PayloadUtf8", "PayloadHash", "PayloadCompress", "PayloadReserve", "PayloadOpen", "PayloadWrite", "PayloadFlush", "PayloadIndex",
                    "AtomicWrite", "AtomicUtf8", "AtomicOpen", "AtomicWriteBytes", "AtomicFlush", "AtomicReplace", "EventAppend", "RecordJson", "JsonEncode",
                    "SourceIndex", "StorageReserve", "LeaseRelease", "TerminalGateWait", "TerminalGate", "MemoryRelease", "FilesGateWait", "StreamWrite" })
                    Assert.That(Stage(work, stage), Is.Not.Null, stage);
                Assert.That((long)Stage(work, "TerminalGate")["maxEndTicks"], Is.LessThanOrEqualTo((long)Stage(work, "MemoryRelease")["maxStartTicks"]));
                Assert.That((long)work["endedTicks"], Is.GreaterThanOrEqualTo((long)Stage(work, "MemoryRelease")["maxEndTicks"]));
                AssertTimingConservation(work);
                var written = work["flushedBlocks"].Single();
                Assert.That((ulong)written["firstSequence"], Is.EqualTo(1));
                Assert.That((ulong)written["lastSequence"], Is.EqualTo(1));
                Assert.That((string)written["source"]["captureId"], Is.EqualTo("capture"));
                Assert.That(report["serviceDetails"]["background"], Is.Not.Empty, "Startup and close have independent observations.");
                Assert.That(store.LastFailure, Is.Null);
            }
            finally { DeleteDirectory(directory); }
        }

        [Test] public void SealedAdvanceIdentityUsesAllEntriesAndKeepsEarlierFlushedRangeSeparate()
        {
            string directory = TemporaryDirectory();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            try
            {
                using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { ObserveQueue = true });
                Assert.That(store.Schedule(512, () => { entered.Set(); if (!release.Wait(5000)) throw new TimeoutException(); }), Is.True);
                Assert.That(entered.Wait(5000), Is.True);
                Assert.That(store.TryWrite(Record(1, "replay.input", new object[] { 1 })), Is.True);
                Assert.That(store.TryWriteAdvance("capture", "run", 1, Advance(2, 0)), Is.True);
                Assert.That(store.TryWriteAdvance("capture", "run", 1, Advance(3, 1)), Is.True);
                Assert.That(store.TryWrite(Record(4, "replay.checkpoint", new { state = 4 })), Is.True);
                release.Set(); Close(store);
                var report = Export(store, directory);
                var advance = WorkItems(report).Single(item => (string)item["kind"] == "Advance");
                Assert.That((ulong)advance["firstSequence"], Is.EqualTo(2));
                Assert.That((ulong)advance["lastSequence"], Is.EqualTo(3));
                Assert.That((int)advance["logicalCount"], Is.EqualTo(2));
                Assert.That((bool)advance["mixedPhases"], Is.True);
                Assert.That(advance["phase"].Type, Is.EqualTo(JTokenType.Null), "Mixed phases must not be presented as one phase.");
                Assert.That((string)advance["engine"], Is.EqualTo("gateway"));
                Assert.That((string)advance["operation"], Is.EqualTo("Advance"));
                var flushed = advance["flushedBlocks"].ToArray();
                Assert.That(flushed, Has.Length.EqualTo(2));
                Assert.That((ulong)flushed[0]["firstSequence"], Is.EqualTo(1), "The trigger work must not overwrite the earlier pending input's range.");
                Assert.That((ulong)flushed[0]["lastSequence"], Is.EqualTo(1));
                Assert.That((int)flushed[0]["recordCount"], Is.EqualTo(1));
                Assert.That((string)flushed[1]["kind"], Is.EqualTo("Advance"));
                Assert.That((ulong)flushed[1]["firstSequence"], Is.EqualTo(2));
                Assert.That((ulong)flushed[1]["lastSequence"], Is.EqualTo(3));
                Assert.That((int)flushed[1]["recordCount"], Is.EqualTo(2));
                foreach (string stage in new[] { "BlockEncode", "BlockWorkspace", "BlockBuild", "BlockHash", "BlockCompress", "BlockHeader" })
                    Assert.That(Stage(advance, stage), Is.Not.Null, stage);
                AssertTimingConservation(advance);
                Assert.That(ReadRecords(directory).Select(record => record.recordSequence), Is.EqualTo(new[] { "1", "2", "3", "4" }));
                Assert.That(store.Dropped, Is.Zero);
            }
            finally { release.Set(); DeleteDirectory(directory); }
        }

        [Test] public void SharedLeaseAndPayloadPersistenceRemainOwnedByTheirOriginalRecord()
        {
            string directory = TemporaryDirectory();
            var memory = new DiagnosticMemoryBudget();
            try
            {
                using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { ObserveQueue = true, Memory = memory });
                Assert.That(SharedEvidencePayload.TryCapture(new CombatSubmissionBatch { Results = new[] { new CombatResult { Damage = 7 } } }, memory, out var shared), Is.True);
                using (shared) Assert.That(store.TryWrite(Record(1, "replay.input", new object[] { shared })), Is.True);
                Close(store);
                var report = Export(store, directory);
                var work = WorkItems(report).Single(item => (string)item["kind"] == "Record");
                Assert.That((string)work["stage"], Is.EqualTo("replay.input"));
                foreach (string stage in new[] { "SharedInputs", "Payload", "PayloadFlush", "LeaseRelease", "MemoryRelease" })
                    Assert.That(Stage(work, stage), Is.Not.Null, stage);
                Assert.That((long)Stage(work, "LeaseRelease")["maxEndTicks"], Is.LessThanOrEqualTo((long)Stage(work, "MemoryRelease")["maxStartTicks"]));
                Assert.That(Directory.GetFiles(directory, "*.json.gz", SearchOption.AllDirectories), Has.Length.EqualTo(1));
                Assert.That(memory.Used, Is.Zero);
                Assert.That(store.LastFailure, Is.Null);
            }
            finally { DeleteDirectory(directory); }
        }

        [Test] public void AtomicReplaceFailureCountsThreeRetriesAndPreservesOriginalFailure()
        {
            string directory = TemporaryDirectory();
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "locked.json"); File.WriteAllText(path, "original");
                using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { ObserveQueue = true });
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Assert.That(store.Schedule(512, () => EvidenceJson.AtomicWrite(path, "replacement")), Is.True);
                    Close(store);
                }
                var work = WorkItems(Export(store, directory)).Single();
                Assert.That((string)work["kind"], Is.EqualTo("Schedule"));
                Assert.That((long)work["ioFailureCount"], Is.EqualTo(4));
                Assert.That((long)work["retryCount"], Is.EqualTo(3));
                Assert.That((long)work["lastRetryHResult"], Is.Not.Zero);
                Assert.That(Stage(work, "AtomicRetryWait"), Is.Not.Null);
                Assert.That(store.LastFailure, Does.Contain("EvidenceMetadataWriteFailed"));
                Assert.That(File.ReadAllText(path), Is.EqualTo("original"));
                AssertTimingConservation(work);
            }
            finally { DeleteDirectory(directory); }
        }

        [Test] public void ObservationDoesNotChangePersistedRecordsOrBlockEncoding()
        {
            string disabled = TemporaryDirectory(), enabled = TemporaryDirectory();
            try
            {
                WriteEquivalent(disabled, false); WriteEquivalent(enabled, true);
                string[] Lines(string root) => Directory.GetFiles(root, "events-*.jsonl", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).SelectMany(File.ReadAllLines).ToArray();
                Assert.That(Lines(enabled), Is.EqualTo(Lines(disabled)));
                var left = Directory.GetFiles(disabled, "*.json.gz", SearchOption.AllDirectories).Single();
                var right = Directory.GetFiles(enabled, "*.json.gz", SearchOption.AllDirectories).Single();
                Assert.That(Path.GetFileName(right), Is.EqualTo(Path.GetFileName(left)));
                Assert.That(File.ReadAllBytes(right), Is.EqualTo(File.ReadAllBytes(left)));
            }
            finally { DeleteDirectory(disabled); DeleteDirectory(enabled); }
        }

        private static void WriteEquivalent(string directory, bool observe)
        {
            using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { ObserveQueue = observe });
            Assert.That(store.TryWrite(Record(1, "replay.checkpoint", new { state = 7 })), Is.True);
            Assert.That(store.TryWriteAdvance("capture", "run", 1, Advance(2, 0)), Is.True);
            Assert.That(store.TryWriteAdvance("capture", "run", 1, Advance(3, 1)), Is.True);
            Close(store);
            Assert.That(store.LastFailure, Is.Null); Assert.That(store.Dropped, Is.Zero);
            if (observe) Export(store, directory);
        }
        private static DiagnosticRecord Record(int sequence, string stage, object input) => new DiagnosticRecord {
            captureId = "capture", runId = "run", round = 1, recordSequence = sequence.ToString(), stage = stage,
            engine = "gateway", operation = "ProcessBatch", input = input, critical = true, estimatedBytes = 16384 };
        private static DiagnosticAdvance Advance(ulong sequence, int phase) => new DiagnosticAdvance {
            role = "Owner", engine = "gateway", operation = "Advance", sequence = sequence, utcTicks = 638000000000000000,
            phase = phase, delta = .02f, frame = 1, fixedStep = 1 };
        private static string TemporaryDirectory() => Path.Combine(Path.GetTempPath(), "evidence-service-" + Guid.NewGuid().ToString("N"));
        private static void DeleteDirectory(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); }
        private static void Close(CombatEvidenceStore store) { store.Dispose(); Assert.That(store.WaitForClose(), Is.True); }
        private static JObject Export(CombatEvidenceStore store, string directory)
        {
            string path = Path.Combine(directory, "observation.json");
            Assert.That(store.ExportQueueObservation(path), Is.True); return JObject.Parse(File.ReadAllText(path));
        }
        private static JToken[] WorkItems(JObject report) => report["serviceDetails"]["workItems"].ToArray();
        private static JToken Stage(JToken work, string name) => work["stages"].SingleOrDefault(stage => (string)stage["stage"] == name);
        private static void AssertTimingConservation(JToken work)
        {
            Assert.That((bool)work["stagesComplete"], Is.True);
            long exclusive = work["stages"].Sum(stage => (long)stage["exclusiveTicks"]);
            Assert.That(exclusive + (long)work["wallResidualTicks"], Is.EqualTo((long)work["wallTicks"]));
            Assert.That((long)work["wallResidualTicks"], Is.GreaterThanOrEqualTo(0));
        }
        private static DiagnosticRecord[] ReadRecords(string directory) => Directory.GetFiles(directory, "events-*.jsonl", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal).SelectMany(File.ReadLines).SelectMany(EvidenceBlocks.Decode).ToArray();
    }
}
