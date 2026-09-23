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
    public sealed class CombatEvidenceRecoveryTests
    {
        private string directory;
        [SetUp] public void SetUp() => directory = Path.Combine(Path.GetTempPath(), "evidence-recovery-" + Guid.NewGuid().ToString("N"));
        [TearDown] public void TearDown() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        [Test] public void FailedFlushPublishesItsRangeAndRequiresANewDurableCheckpoint()
        {
            var storage = new FlushControl();
            using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { Storage = storage });
            Assert.That(store.TryWrite(Record(1)), Is.True);
            Assert.That(SpinWait.SpinUntil(() => Read()?["failure"]?.Type == JTokenType.String, 5000), Is.True);
            var failed = Read();
            Assert.That((string)failed["failureFirstSequence"], Is.EqualTo("1"));
            Assert.That((string)failed["failureLastSequence"], Is.EqualTo("1"));
            Assert.That((bool?)failed["recoveryPending"], Is.True);
            Assert.That((long?)failed["coverageRevision"], Is.GreaterThan(0));
            storage.fail = false;
            Assert.That(store.TryWrite(Record(2)), Is.True);
            Assert.That(SpinWait.SpinUntil(() => (string)Read()?["flushed"] == "2", 5000), Is.True);
            Assert.That((bool?)Read()["recoveryPending"], Is.True, "An ordinary successful write is not a replay checkpoint.");
            var checkpoint = Record(3); checkpoint.stage = "replay.checkpoint"; checkpoint.engine = "*";
            checkpoint.input = new ReplayCheckpointSet { engines = new[] { new ReplayCheckpoint { engine = "status-1", domain = "status", state = new { time = 0 } } } };
            Assert.That(store.TryWrite(checkpoint), Is.True);
            Assert.That(SpinWait.SpinUntil(() => (string)Read()?["reliableFromSequence"] == "3", 5000), Is.True);
            Assert.That((bool?)Read()["recoveryPending"], Is.False);
            store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
            Assert.That((bool)Read()["complete"], Is.False, "Recovery cannot erase the historical loss.");
            Assert.That(((JArray)Read()["gaps"]).Any(g => (string)g["first"] == "1"), Is.True);
        }

        [Test] public void ARecoveryCheckpointWhoseFlushFailsCannotAdvertiseAReplayableStart()
        {
            var storage = new FlushControl();
            using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { Storage = storage });
            Assert.That(store.TryWrite(Record(1)), Is.True);
            Assert.That(SpinWait.SpinUntil(() => Read()?["failure"]?.Type == JTokenType.String, 5000), Is.True);
            long firstEpoch = (long?)Read()["failureEpoch"] ?? 0;
            var checkpoint = Record(2); checkpoint.stage = "replay.checkpoint";
            checkpoint.input = new ReplayCheckpointSet { engines = Array.Empty<ReplayCheckpoint>() };
            Assert.That(store.TryWrite(checkpoint), Is.True);
            Assert.That(SpinWait.SpinUntil(() => (string)Read()?["failureLastSequence"] == "2", 5000), Is.True);
            Assert.That((long?)Read()["failureEpoch"], Is.GreaterThan(firstEpoch));
            Assert.That((bool?)Read()["recoveryPending"], Is.True);
            Assert.That((string)Read()["reliableFromSequence"], Is.Null);
            store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
        }

        [Test] public void CheckpointQueuedBeforeANewerFailureCannotClearRecoveryPending()
        {
            using var store = new CombatEvidenceStore(directory);
            using var writerEntered = new ManualResetEventSlim();
            using var resumeWriter = new ManualResetEventSlim();
            Assert.That(store.Schedule(512, () => { writerEntered.Set(); resumeWriter.Wait(5000); }), Is.True);
            Assert.That(writerEntered.Wait(5000), Is.True);
            try
            {
                store.ReportCaptureFailure(Record(1));
                var checkpoint = Record(2); checkpoint.stage = "replay.checkpoint";
                checkpoint.input = new ReplayCheckpointSet { engines = new[] { new ReplayCheckpoint {
                    engine = "status-1", domain = "status", state = new { time = 0 } } } };
                Assert.That(store.TryWrite(checkpoint), Is.True);
                store.ReportCaptureFailure(Record(3));
            }
            finally { resumeWriter.Set(); }
            Assert.That(SpinWait.SpinUntil(() => (string)Read()?["flushed"] == "2", 5000), Is.True);
            Assert.That((bool?)Read()["recoveryPending"], Is.True, "The frozen snapshot predates the second failure.");
            Assert.That((string)Read()["reliableFromSequence"], Is.Null);
            store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
        }

        [Test] public void EmptyRecoveryCheckpointDoesNotClaimAllEnginesWereCaptured()
        {
            using var store = new CombatEvidenceStore(directory);
            store.ReportCaptureFailure(Record(1));
            var checkpoint = Record(2); checkpoint.stage = "replay.checkpoint";
            checkpoint.input = new ReplayCheckpointSet { engines = Array.Empty<ReplayCheckpoint>() };
            Assert.That(store.TryWrite(checkpoint), Is.True);
            store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
            Assert.That((bool?)Read()["recoveryPending"], Is.True);
            Assert.That((string)Read()["reliableFromSequence"], Is.Null);
        }

        private JObject Read()
        {
            try { string path = Path.Combine(directory, "run", "1", "sources", "capture", "coverage.json"); return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : null; }
            catch (IOException) { return null; }
        }
        private static DiagnosticRecord Record(int sequence) => new DiagnosticRecord { captureId = "capture", runId = "run", round = 1,
            recordSequence = sequence.ToString(), role = "Process", stage = "test.record", critical = true, estimatedBytes = 4096 };
        private sealed class FlushControl : IEvidenceStorage
        {
            private readonly FileEvidenceStorage real = new();
            public volatile bool fail = true;
            public Stream OpenAppend(string path) => real.OpenAppend(path);
            public void Flush(Stream stream, bool durable) { if (fail) throw new IOException("InjectedRecoveryFlushFailure"); real.Flush(stream, durable); }
        }
    }
}
