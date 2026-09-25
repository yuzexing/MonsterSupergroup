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
    public sealed class CombatEvidenceCheckpointAdmissionTests
    {
        [Test]
        public void RecoveryCheckpointReservesCaptureMemoryButOnlyQueuesItsActualRetainedSize()
        {
            using var probe = new BlockedStore((24 << 20) + 512);
            probe.Store.ReportCaptureFailure(Record(1, "lost-before-checkpoint", 512));
            var checkpoint = Snapshot();
            int retained = 512 + CombatEvidenceRuntime.RetainedBytes(checkpoint);
            long beforeMemory = probe.Store.Memory.Used, beforePending = probe.Store.PendingBytes;
            int captureCalls = 0;
            Assert.That(beforePending, Is.GreaterThan(24 << 20));
            Assert.That(probe.Store.TryWrite(Record(2, "replay.checkpoint", 8 << 20), false, () => {
                captureCalls++;
                Assert.That(probe.Store.Memory.Used, Is.EqualTo(beforeMemory + (8 << 20)),
                    "Capture must still reserve its full bounded workspace against the 128 MiB memory budget.");
                return checkpoint;
            }), Is.True);
            Assert.That(captureCalls, Is.EqualTo(1));
            Assert.That(retained, Is.LessThan(8192));
            Assert.That(probe.Store.PendingBytes, Is.EqualTo(beforePending + retained));
            Assert.That(probe.Store.Memory.Used, Is.EqualTo(beforeMemory + retained));
            var observation = probe.CloseAndObserve();
            Assert.That((long)observation["logicalRecords"]["accepted"], Is.EqualTo(1));
            Assert.That((long)observation["logicalRecords"]["rejected"], Is.Zero);
            var coverage = probe.Coverage();
            Assert.That((long)coverage["dropped"], Is.EqualTo(1), "The earlier gap must survive successful recovery.");
            Assert.That((string)coverage["reliableFromSequence"], Is.EqualTo("2"));
            Assert.That((bool)coverage["recoveryPending"], Is.False);
            Assert.That((bool)coverage["complete"], Is.False);
            Assert.That((bool)coverage["tailUnknown"], Is.False);
            Assert.That(probe.Records().Single().checkpointRef, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void CapturedCheckpointStillCannotOverfillTheQueueAndReportsItsActualRejectedSize()
        {
            using var probe = new BlockedStore(24 << 20);
            Assert.That(probe.Store.TryWrite(Record(1, "filler", (8 << 20) - 600)), Is.True);
            var checkpoint = Snapshot();
            int retained = 512 + CombatEvidenceRuntime.RetainedBytes(checkpoint);
            long beforeMemory = probe.Store.Memory.Used, beforePending = probe.Store.PendingBytes;
            int captureCalls = 0;
            Assert.That(retained, Is.GreaterThan(600));
            Assert.That(probe.Store.TryWrite(Record(2, "replay.checkpoint", 8 << 20), false, () => {
                captureCalls++; return checkpoint;
            }), Is.False);
            Assert.That(captureCalls, Is.EqualTo(1));
            Assert.That(probe.Store.Memory.Used, Is.EqualTo(beforeMemory), "A rejected captured value releases all of its reservation.");
            Assert.That(probe.Store.PendingBytes, Is.EqualTo(beforePending));
            var observation = probe.CloseAndObserve();
            Assert.That((string)observation["firstRejection"]["guard"], Is.EqualTo("QueueLimit"));
            Assert.That((long)observation["firstRejection"]["context"]["requestedBytes"], Is.EqualTo(retained));
            Assert.That((long)observation["logicalRecords"]["rejected"], Is.EqualTo(1));
            Assert.That(probe.Records().Single().stage, Is.EqualTo("filler"));
            Assert.That((long)probe.Coverage()["dropped"], Is.EqualTo(1));
        }

        [Test]
        public void CaptureMemoryBudgetIsStillCheckedBeforeInvokingTheCheckpointFactory()
        {
            using var probe = new BlockedStore(512, new DiagnosticMemoryBudget(32 << 20));
            long before = probe.Store.Memory.Used;
            int captureCalls = 0;
            Assert.That(probe.Store.TryWrite(Record(1, "replay.engine_checkpoint", 8 << 20), false, () => {
                captureCalls++; return new ReplayCheckpoint { engine = "status-1", domain = "status", state = new { time = 0 } };
            }), Is.False);
            Assert.That(captureCalls, Is.Zero);
            Assert.That(probe.Store.Memory.Used, Is.EqualTo(before));
            Assert.That(probe.Store.Memory.Peak, Is.LessThanOrEqualTo(probe.Store.Memory.Limit));
            var observation = probe.CloseAndObserve();
            Assert.That((string)observation["firstRejection"]["guard"], Is.EqualTo("BudgetReservation"));
            Assert.That((long)observation["firstRejection"]["context"]["requestedBytes"], Is.EqualTo(8 << 20));
            Assert.That((long)observation["logicalRecords"]["captureFailed"], Is.Zero);
        }

        [Test]
        public void CheckpointCaptureExceptionReturnsItsWholeReservationAndKeepsTheGap()
        {
            using var probe = new BlockedStore((24 << 20) + 512);
            long before = probe.Store.Memory.Used;
            Assert.That(probe.Store.TryWrite(Record(1, "replay.checkpoint", 8 << 20), false,
                () => throw new InvalidOperationException("injected-capture-failure")), Is.False);
            Assert.That(probe.Store.Memory.Used, Is.EqualTo(before));
            var observation = probe.CloseAndObserve();
            Assert.That((long)observation["logicalRecords"]["captureFailed"], Is.EqualTo(1));
            Assert.That((long)observation["logicalRecords"]["rejected"], Is.Zero);
            Assert.That((bool)probe.Coverage()["recoveryPending"], Is.True);
            Assert.That((long)probe.Coverage()["dropped"], Is.EqualTo(1));
        }

        private static ReplayCheckpointSet Snapshot() => new() { engines = new[] {
            new ReplayCheckpoint { engine = "status-1", domain = "status", state = new StatusController(_ => { }).CaptureReplayState() }
        } };

        private static DiagnosticRecord Record(int sequence, string stage, int bytes) => new() {
            captureId = "capture", runId = "run", round = 1, recordSequence = sequence.ToString(), role = "Process",
            stage = stage, engine = stage == "replay.checkpoint" ? "*" : "status-1", critical = true, estimatedBytes = bytes
        };

        private sealed class BlockedStore : IDisposable
        {
            private readonly string root = Path.Combine(Path.GetTempPath(), "checkpoint-admit-" + Guid.NewGuid().ToString("N"));
            private readonly ManualResetEventSlim entered = new(), release = new();
            private int gateTimedOut;
            internal readonly CombatEvidenceStore Store;
            internal BlockedStore(int bytes, DiagnosticMemoryBudget memory = null)
            {
                Store = new CombatEvidenceStore(root, new EvidenceStoreOptions { ObserveQueue = true, Memory = memory ?? new DiagnosticMemoryBudget() });
                Assert.That(Store.Schedule(bytes, () => {
                    entered.Set(); if (!release.Wait(5000)) Interlocked.Exchange(ref gateTimedOut, 1);
                }), Is.True);
                Assert.That(entered.Wait(5000), Is.True);
            }
            internal JObject CloseAndObserve()
            {
                release.Set(); Store.RequestClose();
                Assert.That(Store.WaitForClose(5000), Is.True);
                Assert.That(Volatile.Read(ref gateTimedOut), Is.Zero);
                Assert.That(Store.LastFailure, Is.Null);
                Assert.That(Store.PendingBytes, Is.Zero);
                string path = Path.Combine(root, "observation.json");
                Assert.That(Store.ExportQueueObservation(path), Is.True);
                Assert.That(Store.Memory.Used, Is.Zero);
                var result = JObject.Parse(File.ReadAllText(path));
                Assert.That((bool)result["countsBalanced"], Is.True);
                return result;
            }
            internal JObject Coverage() => JObject.Parse(File.ReadAllText(Path.Combine(root, "run/1/sources/capture/coverage.json")));
            internal DiagnosticRecord[] Records() => Directory.GetFiles(root, "events-*.jsonl", SearchOption.AllDirectories)
                .SelectMany(File.ReadLines).SelectMany(EvidenceBlocks.Decode).ToArray();
            public void Dispose()
            {
                release.Set(); Store.RequestClose();
                bool joined = Store.WaitForClose(5000);
                if (joined) { Store.QueueObservation?.ReleaseAfterStop(true, true); entered.Dispose(); release.Dispose(); }
                Assert.That(joined, Is.True);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
