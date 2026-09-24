using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatEvidenceQueueIntegrationTests
    {
        private string directory;
        [SetUp] public void SetUp() => directory = Path.Combine(Path.GetTempPath(), "queue-observation-" + Guid.NewGuid().ToString("N"));
        [TearDown] public void TearDown() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        [TestCase("Stopping")]
        [TestCase("OversizedRecord")]
        [TestCase("QueueLimit")]
        [TestCase("BudgetReservation")]
        public void FirstRefusalCapturesTheActualGuardAndSourceWatermarks(string expected)
        {
            var memory = new DiagnosticMemoryBudget(expected == "BudgetReservation" ? (24L << 20) + (512 << 10) : 128L << 20);
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions {
                Memory = memory, QueueBytes = 8192, ReservedBytes = 4096, ObserveQueue = true });
            try
            {
                Assert.That(store.QueueObservation, Is.Not.Null);
                if (expected == "Stopping") { store.Dispose(); Assert.That(store.WaitForClose(), Is.True); }
                if (expected == "QueueLimit")
                {
                    Assert.That(store.Schedule(1024, () => { entered.Set(); release.Wait(5000); }), Is.True);
                    Assert.That(entered.Wait(5000), Is.True);
                }
                int bytes = expected == "OversizedRecord" || expected == "Stopping" ? 16000 : expected == "QueueLimit" ? 4096 : 512;
                Assert.That(store.TryWrite(Record(42, bytes, false)), Is.False);
            }
            finally { release.Set(); }
            var report = CloseAndRead(store);
            var first = report["firstRejection"];
            Assert.That((string)first["guard"], Is.EqualTo(expected));
            var context = first["context"];
            Assert.That((string)context["runId"], Is.EqualTo("run"));
            Assert.That((string)context["captureId"], Is.EqualTo("capture"));
            Assert.That((ulong?)context["sequence"], Is.EqualTo(42));
            Assert.That((ulong?)context["produced"], Is.EqualTo(42));
            Assert.That((ulong?)context["written"], Is.Zero);
            Assert.That((ulong?)context["flushed"], Is.Zero);
            Assert.That((long?)context["requestedBytes"], Is.GreaterThan(0));
            Assert.That(memory.Used, Is.Zero);
            Assert.That(store.TryWrite(Record(43)), Is.False, "An exported observer must tolerate later rejected calls.");
        }

        [Test] public void ScheduleRefusalDoesNotInventASourceOrSequence()
        {
            using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { ObserveQueue = true, QueueBytes = 8192, ReservedBytes = 4096 });
            Assert.That(store.Schedule(8192, () => { }), Is.False);
            var first = CloseAndRead(store)["firstRejection"];
            Assert.That((string)first["entry"], Is.EqualTo("Schedule"));
            foreach (string name in new[] { "runId", "captureId", "round", "sequence", "phase", "produced", "written", "flushed" })
                Assert.That(first["context"][name]?.Type == JTokenType.Null || first["context"][name] == null, Is.True, name);
        }

        [Test] public void CaptureExceptionIsNotAQueueGuardRefusal()
        {
            using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { ObserveQueue = true });
            Assert.That(store.TryWrite(Record(1), true, () => throw new InvalidOperationException("InjectedCaptureFailure")), Is.False);
            var report = CloseAndRead(store);
            Assert.That(report["firstRejection"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That((long)report["logicalRecords"]["captureFailed"], Is.EqualTo(1));
            Assert.That((long)report["logicalRecords"]["rejected"], Is.Zero);
            Assert.That(store.Dropped, Is.EqualTo(1));
            Assert.That(store.Memory.Used, Is.Zero);
        }

        [Test] public void SharedAdvanceWorkItemKeepsEveryInputCompletionAndReleasesOnce()
        {
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { ObserveQueue = true });
            try
            {
                Assert.That(store.Schedule(512, () => { entered.Set(); release.Wait(5000); }), Is.True);
                Assert.That(entered.Wait(5000), Is.True);
                for (ulong i = 1; i <= 4; i++) Assert.That(store.TryWriteAdvance("capture", "run", 1, new DiagnosticAdvance {
                    sequence = i, role = "status", engine = "status-1", operation = "Advance", phase = (int)((i - 1) % 2),
                    utcTicks = DateTime.UtcNow.Ticks, delta = 1f / 144, frame = (int)((i - 1) / 2) }), Is.True);
            }
            finally { release.Set(); }
            var report = CloseAndRead(store);
            Assert.That((long)report["logicalRecords"]["accepted"], Is.EqualTo(4));
            Assert.That((long)report["producerTotals"]["workEnqueued"], Is.EqualTo(2));
            Assert.That((long)report["consumerTotals"]["workCompleted"], Is.EqualTo(2));
            Assert.That((long)report["producerTotals"]["chargedBytes"], Is.EqualTo((long)report["consumerTotals"]["releasedBytes"]));
            var records = Directory.GetFiles(directory, "events-*.jsonl", SearchOption.AllDirectories).SelectMany(File.ReadLines).SelectMany(EvidenceBlocks.Decode).ToArray();
            Assert.That(records.Select(r => r.recordSequence), Is.EqualTo(new[] { "1", "2", "3", "4" }));
            Assert.That(records.Select(r => r.stage), Is.EqualTo(new[] { "replay.input", "replay.output", "replay.input", "replay.output" }));
            Assert.That(store.Memory.Used, Is.Zero);
        }

        [Test] public void LiveConsumerPreventsExportAndReservationRelease()
        {
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { ObserveQueue = true });
            try
            {
                Assert.That(store.Schedule(512, () => { entered.Set(); release.Wait(5000); }), Is.True);
                Assert.That(entered.Wait(5000), Is.True);
                store.Dispose(); Assert.That(store.WaitForClose(1), Is.False);
                long held = store.Memory.Used;
                Assert.That(store.ExportQueueObservation(Path.Combine(directory, "premature.json")), Is.False);
                Assert.That(store.Memory.Used, Is.EqualTo(held));
                Assert.That(File.Exists(Path.Combine(directory, "premature.json")), Is.False);
            }
            finally { release.Set(); }
            CloseAndRead(store); Assert.That(store.Memory.Used, Is.Zero);
        }

        [Test] public void FailingProfilerHooksCannotSkipWorkOrLeakItsReservation()
        {
            var start = EvidenceWorkerProfiling.Started; var stop = EvidenceWorkerProfiling.Stopped;
            var begin = EvidenceWorkerProfiling.WorkStarted; var end = EvidenceWorkerProfiling.WorkFinished;
            long failures = EvidenceWorkerProfiling.FailureCount;
            CombatEvidenceStore store = null;
            try
            {
                EvidenceWorkerProfiling.Started = (_, _) => throw new InvalidOperationException("Fixture hook");
                EvidenceWorkerProfiling.Stopped = EvidenceWorkerProfiling.WorkStarted = EvidenceWorkerProfiling.WorkFinished =
                    () => throw new InvalidOperationException("Fixture hook");
                store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { ObserveQueue = true });
                Assert.That(store.TryWrite(Record(1)), Is.True);
                CloseAndRead(store);
                Assert.That(Directory.GetFiles(directory, "events-*.jsonl", SearchOption.AllDirectories)
                    .SelectMany(File.ReadLines).SelectMany(EvidenceBlocks.Decode).Single().recordSequence, Is.EqualTo("1"));
                Assert.That(store.Memory.Used, Is.Zero);
                Assert.That(EvidenceWorkerProfiling.FailureCount, Is.GreaterThan(failures));
            }
            finally
            {
                store?.Dispose(); store?.WaitForClose();
                EvidenceWorkerProfiling.Started = start; EvidenceWorkerProfiling.Stopped = stop;
                EvidenceWorkerProfiling.WorkStarted = begin; EvidenceWorkerProfiling.WorkFinished = end;
            }
        }

        [Test] public void FlushStallAndFollowingRefusalAreObservedWithoutChangingTheGap()
        {
            using var storage = new BlockingFlush();
            using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { ObserveQueue = true, Storage = storage,
                QueueBytes = 65536, ReservedBytes = 0 });
            try
            {
                Assert.That(store.TryWrite(Record(1)), Is.True);
                Assert.That(storage.entered.Wait(5000), Is.True);
                // This span crosses at least one 100ms window while the writer is inside durable flush.
                Thread.Sleep(120);
                Assert.That(store.TryWrite(Record(2, 65536)), Is.True);
                Assert.That(store.TryWrite(Record(3)), Is.False);
            }
            finally { storage.release.Set(); }
            var report = CloseAndRead(store);
            Assert.That((string)report["firstRejection"]["guard"], Is.EqualTo("QueueLimit"));
            Assert.That((ulong?)report["firstRejection"]["context"]["sequence"], Is.EqualTo(3));
            Assert.That((long)report["consumerTotals"]["durableFlushTicks"], Is.GreaterThanOrEqualTo(Stopwatch.Frequency / 10));
            Assert.That(store.Dropped, Is.EqualTo(1));
            var coverage = EvidenceJson.Decode<EvidenceCoverage>(File.ReadAllText(Directory.GetFiles(directory, "coverage.json", SearchOption.AllDirectories).Single()));
            Assert.That(coverage.complete, Is.False);
            Assert.That(coverage.gaps.Any(g => ulong.Parse(g.first) <= 3 && ulong.Parse(g.last) >= 3), Is.True);
            Assert.That(store.Memory.Used, Is.Zero);
        }

        private JObject CloseAndRead(CombatEvidenceStore store)
        {
            store.Dispose(); Assert.That(store.WaitForClose(10000), Is.True);
            string path = Path.Combine(directory, "observation.json");
            Directory.CreateDirectory(directory);
            Assert.That(store.ExportQueueObservation(path), Is.True);
            return JObject.Parse(File.ReadAllText(path));
        }
        private static DiagnosticRecord Record(int sequence, int bytes = 512, bool critical = true) => new DiagnosticRecord {
            runId = "run", captureId = "capture", round = 1, recordSequence = sequence.ToString(), stage = "process.start",
            input = "fixture", critical = critical, estimatedBytes = bytes };
        private sealed class BlockingFlush : IEvidenceStorage, IDisposable
        {
            public readonly ManualResetEventSlim entered = new(), release = new();
            private readonly FileEvidenceStorage actual = new();
            public Stream OpenAppend(string path) => actual.OpenAppend(path);
            public void Flush(Stream stream, bool durable) { entered.Set(); if (!release.Wait(5000)) throw new TimeoutException("Fixture flush release"); actual.Flush(stream, durable); }
            public void Dispose() { release.Set(); entered.Dispose(); release.Dispose(); }
        }
    }
}
