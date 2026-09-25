using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatEvidenceShutdownTests
    {
        [Test] public void DiagnosticLocalPrefixRetentionCannotReportCompleteAfterRealDeletion()
        {
            string directory = Path.Combine(Path.GetTempPath(), "evidence-retention-shutdown-" + Guid.NewGuid().ToString("N"));
            var options = RetentionOptions();
            var store = new CombatEvidenceStore(directory, options);
            try
            {
                Assert.That(store.TryWrite(RetentionRecord(1, "run")), Is.True);
                var checkpoint = RetentionRecord(2, "run"); checkpoint.stage = "replay.checkpoint";
                checkpoint.input = new ReplayCheckpointSet { engines = new[] {
                    new ReplayCheckpoint { engine = "gateway-1", domain = "gateway", state = new GatewayReplayState() } } };
                Assert.That(store.TryWrite(checkpoint), Is.True);
                Assert.That(store.TryWrite(RetentionRecord(3, "run")), Is.True);
                FlushOnWriter(store);
                Assert.That(ReadRetainedRecords(directory).Select(r => r.recordSequence), Does.Contain("1"));
                RunOnWriter(store, () => StoreMethod("Prune").Invoke(store, new object[] { options.SessionBytes }));
                store.RequestClose(); Assert.That(store.WaitForClose(5000), Is.True);
                var records = ReadRetainedRecords(directory);
                Assert.That(records.Select(r => r.recordSequence), Does.Not.Contain("1"));
                Assert.That(records.Select(r => r.recordSequence), Does.Contain("2").And.Contain("3"));
                Assert.That(Directory.GetFiles(directory, "retention.json", SearchOption.AllDirectories), Has.Length.EqualTo(1));
                Assert.That((bool)ReadCoverage(directory, "run")["complete"], Is.True, "Durable latest watermarks alone cannot prove the deleted prefix remains available.");
                var assessment = store.AssessShutdownCompleteness();
                Assert.That(assessment.countsBalanced, Is.True);
                Assert.That(assessment.complete, Is.False);
                Assert.That(string.Join(";", assessment.failures), Does.Contain("LocalCaptureRetention:CapacityRetention:run/"));
            }
            finally { DisposeRetentionStore(store, directory); }
        }

        [TestCase(false)] [TestCase(true)]
        public void DiagnosticGlobalRetentionDistinguishesCurrentContextsFromUnrelatedHistoricalRuns(bool unrelatedHistory)
        {
            string directory = Path.Combine(Path.GetTempPath(), "evidence-global-retention-shutdown-" + Guid.NewGuid().ToString("N"));
            var options = RetentionOptions();
            CombatEvidenceStore store = null;
            try
            {
                if (unrelatedHistory)
                {
                    using var historical = new CombatEvidenceStore(directory);
                    var record = RetentionRecord(1, "old-run"); record.captureId = "old-capture";
                    Assert.That(historical.TryWrite(record), Is.True);
                    historical.RequestClose(); Assert.That(historical.WaitForClose(5000), Is.True);
                }
                store = new CombatEvidenceStore(directory, options);
                if (!unrelatedHistory) Assert.That(store.TryWrite(RetentionRecord(1, "old-run")), Is.True);
                Assert.That(store.TryWrite(RetentionRecord(unrelatedHistory ? 1 : 2, "active-run")), Is.True);
                FlushOnWriter(store);
                string oldRun = Path.Combine(directory, "old-run");
                Assert.That(Directory.Exists(oldRun), Is.True);
                Assert.That(ReadRetainedRecords(oldRun), Has.Length.EqualTo(1));
                long totalBytes = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
                // Leave margin for the writer's small periodic coverage rewrites between sampling
                // and pruning, while the old run is much larger than that margin.
                long requiredBytes = options.TotalBytes - totalBytes + 1024;
                Assert.That(requiredBytes, Is.GreaterThan(0));
                RunOnWriter(store, () => StoreMethod("Prune").Invoke(store, new object[] { requiredBytes }));
                Assert.That(Directory.Exists(oldRun), Is.False, "The test must exercise actual global deletion.");
                Assert.That(File.ReadAllText(Path.Combine(directory, "retention.jsonl")), Does.Contain("old-run"));
                store.RequestClose(); Assert.That(store.WaitForClose(5000), Is.True);
                Assert.That(ReadRetainedRecords(directory), Has.Length.EqualTo(1));
                Assert.That((bool)ReadCoverage(directory, "active-run")["complete"], Is.True);
                var assessment = store.AssessShutdownCompleteness();
                Assert.That(assessment.sources, Is.EqualTo(1), "The globally pruned health entry has actually been removed.");
                Assert.That(assessment.countsBalanced, Is.True);
                Assert.That(assessment.complete, Is.EqualTo(unrelatedHistory));
                if (!unrelatedHistory)
                    Assert.That(string.Join(";", assessment.failures), Does.Contain("LocalCaptureRetention:GlobalCapacityRetention:old-run"));
                else Assert.That(assessment.failures, Is.Empty);
            }
            finally { DisposeRetentionStore(store, directory); }
        }

        [Test] public void DiagnosticQuitRequestReleasesThePointerAndRepeatedRequestsRestoreIt()
        {
            bool previousVisible = Cursor.visible;
            var previousLock = Cursor.lockState;
            try
            {
                using var capture = new RuntimeCapture(true);
                capture.BlockWriter();
                Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
                Assert.That(capture.WantsToQuit(), Is.False);
                Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
                Assert.That(Cursor.visible, Is.True);
                // A later menu callback can reclaim the pointer while the writer is still draining.
                Cursor.lockState = CursorLockMode.Confined; Cursor.visible = false;
                Assert.That(capture.WantsToQuit(), Is.False);
                Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
                Assert.That(Cursor.visible, Is.True);
                capture.ReleaseAndJoin(); capture.FinishDiagnostic();
                capture.AssertSingleStopAndCompleteCoverage();
            }
            finally { Cursor.lockState = previousLock; Cursor.visible = previousVisible; }
        }

        [Test] public void DiagnosticDrainWaitsBeyondThirtySecondsAndResumesWithoutDuplicateStop()
        {
            using var capture = new RuntimeCapture(true);
            capture.BlockWriter();
            Assert.That(capture.WantsToQuit(), Is.False);
            Assert.That(capture.WantsToQuit(), Is.False);
            Assert.That(capture.Poll(capture.Started + 1), Is.False);
            Assert.That(capture.Poll(capture.Started + 61), Is.False);
            Assert.That((bool)Field("shutdownStalled").GetValue(capture.Runtime), Is.True);
            Assert.That(File.Exists(capture.StatusPath), Is.False);
            capture.ReleaseAndJoin();
            capture.FinishDiagnostic();
            Assert.That(capture.Poll(capture.Started + 65), Is.True);
            Assert.That(capture.WantsToQuit(), Is.True);
            Assert.That((bool)capture.ShutdownStatus()["complete"], Is.True);
            Assert.That((string)capture.Status()["evidenceCompleteness"], Is.EqualTo("Complete"));
            capture.AssertSingleStopAndCompleteCoverage();
        }

        [Test] public void DiagnosticFinalizationCannotBlockUnityPollingOnTheStoreGate()
        {
            using var capture = new RuntimeCapture(true);
            capture.WantsToQuit(); capture.ReleaseAndJoin();
            object producerGate = typeof(CombatEvidenceStore).GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(capture.Store);
            lock (producerGate)
            {
                Assert.That(capture.Poll(capture.Started + 1), Is.False);
                Assert.That(((Thread)Field("diagnosticFinalizer").GetValue(capture.Runtime)).IsAlive, Is.True);
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < 1000; i++) Assert.That(capture.Poll(capture.Started + 62), Is.False);
                watch.Stop();
                Assert.That(watch.ElapsedMilliseconds, Is.LessThan(1000));
                Assert.That(Field("diagnosticResult").GetValue(capture.Runtime), Is.Null);
            }
            capture.FinishDiagnostic();
            Assert.That((bool)capture.ShutdownStatus()["complete"], Is.True);
        }

        [Test] public void DiagnosticRejectedRecordRequiresAcknowledgementDespiteAnEmptyFinalQueue()
        {
            using var capture = new RuntimeCapture(true);
            Assert.That(capture.Runtime.TryWrite(new DiagnosticRecord { stage = "test.oversized", estimatedBytes = int.MaxValue }), Is.False);
            capture.WantsToQuit(); capture.ReleaseAndJoin(); capture.FinishDiagnostic();
            Assert.That(capture.Store.PendingBytes, Is.Zero);
            Assert.That(capture.Poll(capture.Started + 120), Is.False);
            Assert.That((bool)capture.ShutdownStatus()["complete"], Is.False);
            Assert.That((string)capture.ShutdownStatus()["status"], Is.EqualTo("Incomplete"));
            Assert.That((long)capture.ShutdownStatus()["assessment"]["dropped"], Is.GreaterThan(0));
            Assert.That(capture.WantsToQuit(), Is.False);
            capture.Acknowledge();
            Assert.That(capture.WantsToQuit(), Is.True);
        }

        [Test] public void DiagnosticUnbalancedObservationCannotPassWithHealthyCoverageAndAnEmptyQueue()
        {
            using var capture = new RuntimeCapture(true);
            capture.WantsToQuit(); capture.ReleaseAndJoin();
            capture.Store.QueueObservation.Attempt(EvidenceQueueEntry.TryWrite, EvidenceQueueObservation.Now);
            capture.FinishDiagnostic();
            capture.AssertSingleStopAndCompleteCoverage();
            Assert.That(capture.Poll(capture.Started + 120), Is.False);
            var status = capture.ShutdownStatus();
            Assert.That((bool)status["complete"], Is.False);
            Assert.That((bool)status["assessment"]["countsBalanced"], Is.False);
            Assert.That(status["assessment"]["failures"].ToString(), Does.Contain("UnbalancedWriterCounts"));
            Assert.That((bool)JObject.Parse(File.ReadAllText(capture.ObservationPath))["countsBalanced"], Is.False);
            capture.Acknowledge();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DiagnosticWriteOrFinalFlushFailureNeverReportsComplete(bool failFlush)
        {
            using var capture = new RuntimeCapture(true, new FailingStorage(failFlush));
            capture.WantsToQuit(); capture.ReleaseAndJoin(); capture.FinishDiagnostic();
            Assert.That(capture.Store.PendingBytes, Is.Zero);
            Assert.That(capture.Poll(capture.Started + 120), Is.False);
            Assert.That((bool)capture.ShutdownStatus()["complete"], Is.False);
            Assert.That((JArray)capture.ShutdownStatus()["assessment"]["failures"], Is.Not.Empty);
            capture.Acknowledge();
            Assert.That(capture.WantsToQuit(), Is.True);
        }

        [Test] public void DiagnosticPersistedCoverageMismatchCannotPassOnHealthyMemoryState()
        {
            using var capture = new RuntimeCapture(true);
            capture.WantsToQuit(); capture.ReleaseAndJoin();
            string coveragePath = Path.Combine(capture.DirectoryPath, "boot", "0", "sources", "capture", "coverage.json");
            var persisted = JObject.Parse(File.ReadAllText(coveragePath)); persisted["flushed"] = "0";
            File.WriteAllText(coveragePath, persisted.ToString());
            capture.FinishDiagnostic();
            Assert.That(capture.Poll(capture.Started + 120), Is.False);
            Assert.That((bool)capture.ShutdownStatus()["complete"], Is.False);
            Assert.That(capture.ShutdownStatus()["assessment"]["failures"].ToString(), Does.Contain("PersistedCoverageMismatch"));
            capture.Acknowledge();
        }

        [Test] public void DiagnosticObservationExportFailureRequiresAcknowledgementWithoutFakeRetry()
        {
            using var capture = new RuntimeCapture(true);
            Directory.CreateDirectory(capture.ObservationPath);
            capture.WantsToQuit(); capture.ReleaseAndJoin(); capture.FinishDiagnostic();
            Assert.That(capture.Poll(capture.Started + 120), Is.False);
            Assert.That((bool)capture.ShutdownStatus()["complete"], Is.False);
            Assert.That((string)capture.ShutdownStatus()["observationStatus"], Is.EqualTo("ExportFailed"));
            Directory.Delete(capture.ObservationPath, false);
            Assert.That(capture.Poll(capture.Started + 200), Is.False, "A failed terminal export is not silently retried by UI polling.");
            Assert.That(File.Exists(capture.ObservationPath), Is.False);
            capture.Acknowledge();
        }

        [Test] public void DiagnosticStatusExportFailureDoesNotDeclareSuccess()
        {
            using var capture = new RuntimeCapture(true);
            Directory.CreateDirectory(capture.ShutdownStatusPath);
            capture.WantsToQuit(); capture.ReleaseAndJoin(); capture.FinishDiagnostic();
            Assert.That(capture.Poll(capture.Started + 120), Is.False);
            Assert.That((bool)capture.Status()["exported"], Is.True);
            Assert.That(capture.WantsToQuit(), Is.False);
            capture.Acknowledge();
            Assert.That(capture.WantsToQuit(), Is.True);
        }

        [Test] public void DiagnosticObservationStatusReadFailureStillPublishesTheTerminalFailureReason()
        {
            using var capture = new RuntimeCapture(true);
            capture.WantsToQuit(); capture.ReleaseAndJoin();
            File.WriteAllText(capture.StatusPath, "{}");
            using (var held = new FileStream(capture.StatusPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                capture.FinishDiagnostic();
                Assert.That(capture.Poll(capture.Started + 120), Is.False);
                Assert.That((bool)capture.ShutdownStatus()["complete"], Is.False);
                Assert.That((string)capture.ShutdownStatus()["status"], Is.EqualTo("Incomplete"));
                Assert.That((string)capture.ShutdownStatus()["error"], Does.StartWith("IOException:"));
            }
            capture.Acknowledge();
        }

        [Test] public void DiagnosticConfirmedAbandonmentIsBoundedAndFallbackNeverWaitsForWriter()
        {
            using var capture = new RuntimeCapture(true);
            capture.BlockWriter(); capture.WantsToQuit();
            capture.Abandon(capture.Started + 5);
            capture.Abandon(capture.Started + 6);
            Assert.That(capture.Poll(capture.Started + 7), Is.True);
            var watch = Stopwatch.StartNew(); capture.Fallback(); watch.Stop();
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(1000));
            Assert.That(capture.Store.WaitForClose(0), Is.False);
            capture.JoinFinalizers();
            var abandoned = JObject.Parse(File.ReadAllText(Path.Combine(capture.DirectoryPath, "shutdown-aborted-capture.json")));
            Assert.That((string)abandoned["status"], Is.EqualTo("UserAborted"));
            Assert.That((bool)abandoned["complete"], Is.False);
            Assert.That((bool)abandoned["overridesOtherShutdownStatus"], Is.True);
            Assert.That((long)abandoned["pendingBytes"], Is.GreaterThan(0));
            Assert.That(File.Exists(capture.ObservationPath), Is.False);
            capture.ReleaseAndJoin();
        }

        [Test] public void QuitRequestStopsProductionWithoutWaitingForBlockedWriter()
        {
            using var capture = new RuntimeCapture();
            capture.BlockWriter();
            var watch = Stopwatch.StartNew();
            Assert.That(capture.WantsToQuit(), Is.False);
            watch.Stop();
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(1000), "The quit callback must not perform the old 2000 ms join.");
            Assert.That(capture.WantsToQuit(), Is.False);
            Assert.That(capture.Poll(capture.Started + 1), Is.False);
            Assert.That(capture.Store.WaitForClose(0), Is.False);
            Assert.That(CombatEvidence.Sink, Is.Null);
            Assert.That(capture.Runtime.TryWrite(new DiagnosticRecord { stage = "after.stop" }), Is.False);
            Assert.That(File.Exists(capture.StatusPath), Is.False, "A pending asynchronous drain is not yet a timeout.");
            capture.ReleaseAndJoin();
            LogAssert.Expect(LogType.Log, "[CombatEvidence] Writer observation exported; evidence completeness remains unverified.");
            Assert.That(capture.Poll(capture.Started + 2), Is.True);
            Assert.That(capture.WantsToQuit(), Is.True);
            capture.AssertSingleStopAndCompleteCoverage();
            Assert.That((bool)capture.Status()["exported"], Is.True);
        }

        [Test] public void DeadlineProducesOneFailureAndLateCompletionPreservesItsOriginalBytes()
        {
            using var capture = new RuntimeCapture();
            capture.BlockWriter();
            Assert.That(capture.WantsToQuit(), Is.False);
            LogAssert.Expect(LogType.Warning, "[CombatEvidence] Writer observation: WriterNotJoined");
            Assert.That(capture.Poll(capture.Started + 31), Is.True);
            byte[] failure = File.ReadAllBytes(capture.StatusPath);
            var status = capture.Status();
            Assert.That((string)status["status"], Is.EqualTo("WriterNotJoined"));
            Assert.That((bool)status["shutdownTimedOut"], Is.True);
            Assert.That((bool)status["exported"], Is.False);
            Assert.That(File.Exists(capture.ObservationPath), Is.False);
            Assert.That((string)status["partialObservationPath"], Is.Not.Null.And.Not.Empty);
            string partialPath = Path.Combine(capture.DirectoryPath, (string)status["partialObservationPath"]);
            byte[] partial = File.ReadAllBytes(partialPath);
            Assert.That(capture.Poll(capture.Started + 60), Is.True);
            Assert.That(capture.Poll(capture.Started + 90), Is.True);
            Assert.That(capture.WantsToQuit(), Is.True);
            Assert.That(File.ReadAllBytes(capture.StatusPath), Is.EqualTo(failure));
            Assert.That(File.ReadAllBytes(partialPath), Is.EqualTo(partial));

            capture.ReleaseAndJoin();
            LogAssert.Expect(LogType.Log, "[CombatEvidence] Writer observation exported; evidence completeness remains unverified.");
            capture.Fallback();
            Assert.That((string)capture.Status()["status"], Is.EqualTo("Exported"));
            Assert.That(File.ReadAllBytes(capture.StatusPath + ".first-failure.json"), Is.EqualTo(failure));
            Assert.That(File.ReadAllBytes(partialPath), Is.EqualTo(partial));
            capture.AssertSingleStopAndCompleteCoverage();
            byte[] success = File.ReadAllBytes(capture.StatusPath);
            byte[] observation = File.ReadAllBytes(capture.ObservationPath);
            capture.Fallback();
            Assert.That(File.ReadAllBytes(capture.StatusPath), Is.EqualTo(success));
            Assert.That(File.ReadAllBytes(capture.ObservationPath), Is.EqualTo(observation));
        }

        [Test] public void RepeatedFallbackCanExportAfterAnEarlierJoinTimeout()
        {
            using var capture = new RuntimeCapture();
            capture.BlockWriter();
            LogAssert.Expect(LogType.Warning, "[CombatEvidence] Writer observation: WriterNotJoined");
            capture.Fallback();
            Assert.That(capture.Store.WaitForClose(0), Is.False);
            byte[] failure = File.ReadAllBytes(capture.StatusPath);
            capture.ReleaseAndJoin();
            LogAssert.Expect(LogType.Log, "[CombatEvidence] Writer observation exported; evidence completeness remains unverified.");
            capture.Fallback();
            Assert.That((bool)capture.Status()["exported"], Is.True);
            Assert.That(File.ReadAllBytes(capture.StatusPath + ".first-failure.json"), Is.EqualTo(failure));
            capture.AssertSingleStopAndCompleteCoverage();
        }

        [Test] public void FailedNumericExportCanRetryWithoutReplacingTheFailureEvidence()
        {
            using var capture = new RuntimeCapture();
            capture.Store.RequestClose();
            Assert.That(capture.Store.WaitForClose(5000), Is.True);
            Directory.CreateDirectory(capture.ObservationPath); // Controlled output-path failure, not an existing successful observation.
            Assert.That(capture.Export(), Is.EqualTo("ExportFailed"));
            byte[] failure = File.ReadAllBytes(capture.StatusPath);
            Directory.Delete(capture.ObservationPath, false);
            Assert.That(capture.Export(), Is.EqualTo("Exported"));
            Assert.That(File.ReadAllBytes(capture.StatusPath + ".first-failure.json"), Is.EqualTo(failure));
            byte[] observation = File.ReadAllBytes(capture.ObservationPath);
            byte[] status = File.ReadAllBytes(capture.StatusPath);
            Assert.That(capture.Export(), Is.EqualTo("StatusAlreadyExists"));
            Assert.That(File.ReadAllBytes(capture.ObservationPath), Is.EqualTo(observation));
            Assert.That(File.ReadAllBytes(capture.StatusPath), Is.EqualTo(status));
        }

        [Test] public void StatusWriteRetryKeepsTheAlreadyExportedNumericFile()
        {
            using var capture = new RuntimeCapture();
            capture.Store.RequestClose();
            Assert.That(capture.Store.WaitForClose(5000), Is.True);
            Directory.CreateDirectory(capture.StatusPath);
            Assert.That(capture.Export(), Does.StartWith("StatusWriteFailed:"));
            byte[] observation = File.ReadAllBytes(capture.ObservationPath);
            Directory.Delete(capture.StatusPath, false);
            Assert.That(capture.Export(), Is.EqualTo("Exported"));
            Assert.That(File.ReadAllBytes(capture.ObservationPath), Is.EqualTo(observation));
            Assert.That((bool)capture.Status()["writerJoined"], Is.True);
        }

        [TestCase("{broken status")]
        [TestCase("{\"exported\":{},\"status\":[],\"partialObservationPath\":{}}")]
        public void InvalidStatusCanRecoverAndPreservesItsExactBytes(string invalidStatus)
        {
            using var capture = new RuntimeCapture();
            capture.Store.RequestClose();
            Assert.That(capture.Store.WaitForClose(5000), Is.True);
            byte[] original = System.Text.Encoding.UTF8.GetBytes(invalidStatus);
            File.WriteAllBytes(capture.StatusPath, original);
            Assert.That(capture.Export(), Is.EqualTo("Exported"));
            Assert.That(File.ReadAllBytes(capture.StatusPath + ".first-failure.json"), Is.EqualTo(original));
            Assert.That((bool)capture.Status()["exported"], Is.True);
            Assert.That(capture.Export(), Is.EqualTo("StatusAlreadyExists"));
        }

        [Test] public void FailedPartialExportCanRetryWithoutChangingTheFirstFailure()
        {
            using var capture = new RuntimeCapture();
            capture.BlockWriter();
            Assert.That(capture.WantsToQuit(), Is.False);
            string partialPath = Path.Combine(capture.DirectoryPath, "writer-observation-partial-capture.json");
            Directory.CreateDirectory(partialPath);
            Assert.That(capture.Export(), Is.EqualTo("WriterNotJoined"));
            byte[] failure = File.ReadAllBytes(capture.StatusPath);
            Assert.That((string)capture.Status()["partialObservationError"], Is.Not.Null.And.Not.Empty);
            Directory.Delete(partialPath, false);
            Assert.That(capture.Export(), Is.EqualTo("WriterNotJoined"));
            Assert.That(File.Exists(partialPath), Is.True);
            Assert.That(File.ReadAllBytes(capture.StatusPath), Is.EqualTo(failure));
            capture.ReleaseAndJoin();
            Assert.That(capture.Export(), Is.EqualTo("Exported"));
            Assert.That((string)capture.Status()["partialObservationPath"], Is.EqualTo(Path.GetFileName(partialPath)));
            Assert.That(File.ReadAllBytes(capture.StatusPath + ".first-failure.json"), Is.EqualTo(failure));
        }

        private static FieldInfo Field(string name) => typeof(CombatEvidenceRuntime).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        private static MethodInfo Method(string name, bool isStatic = false) => typeof(CombatEvidenceRuntime).GetMethod(name,
            BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance));

        private static MethodInfo StoreMethod(string name) => typeof(CombatEvidenceStore).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
        private static EvidenceStoreOptions RetentionOptions()
        {
            var options = EvidenceStoreOptions.ForProfile(EvidenceProfile.Diagnostic, true);
            options.SessionBytes = 100000; options.TotalBytes = 200000; options.SegmentBytes = 4000;
            return options;
        }
        private static DiagnosticRecord RetentionRecord(int sequence, string run) => new DiagnosticRecord {
            schemaVersion = 1, captureId = "capture", runId = run, round = 1, recordSequence = sequence.ToString(),
            stage = "retention.fixture", input = new string('x', 2500), estimatedBytes = 8192, critical = true };
        private static DiagnosticRecord[] ReadRetainedRecords(string directory) => Directory.GetFiles(directory, "events-*.jsonl", SearchOption.AllDirectories)
            .SelectMany(ReadActiveLines).SelectMany(EvidenceBlocks.Decode).ToArray();
        private static System.Collections.Generic.IEnumerable<string> ReadActiveLines(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            string line;
            while ((line = reader.ReadLine()) != null) yield return line;
        }
        private static JObject ReadCoverage(string directory, string run) => JObject.Parse(File.ReadAllText(Path.Combine(directory, run, "1", "sources", "capture", "coverage.json")));
        private static void FlushOnWriter(CombatEvidenceStore store) => RunOnWriter(store, () => StoreMethod("FlushSources").Invoke(store, new object[] { false }));
        private static void RunOnWriter(CombatEvidenceStore store, Action action)
        {
            using var completed = new ManualResetEventSlim();
            Exception failure = null;
            Assert.That(store.Schedule(512, () => {
                try { action(); } catch (Exception error) { failure = error; }
                finally { completed.Set(); }
            }), Is.True);
            Assert.That(completed.Wait(5000), Is.True);
            Assert.That(failure, Is.Null);
        }
        private static void DisposeRetentionStore(CombatEvidenceStore store, string directory)
        {
            if (store != null)
            {
                store.RequestClose(); Assert.That(store.WaitForClose(5000), Is.True);
                store.QueueObservation?.ReleaseAfterStop(true, true); store.Dispose();
            }
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private sealed class RuntimeCapture : IDisposable
        {
            private readonly GameObject root;
            private readonly ManualResetEventSlim entered = new(), release = new();
            private readonly bool previousCursorVisible = Cursor.visible;
            private readonly CursorLockMode previousCursorLock = Cursor.lockState;
            private int gateExpired;
            internal readonly CombatEvidenceRuntime Runtime;
            internal readonly CombatEvidenceStore Store;
            internal readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "evidence-shutdown-" + Guid.NewGuid().ToString("N"));
            internal string StatusPath => Path.Combine(DirectoryPath, "writer-observation-status-capture.json");
            internal string ObservationPath => Path.Combine(DirectoryPath, "writer-observation-capture.json");
            internal double Started => (double)Field("shutdownStartedSeconds").GetValue(Runtime);
            internal string ShutdownStatusPath => Path.Combine(DirectoryPath, "shutdown-status-capture.json");
            internal RuntimeCapture(bool diagnostic = false, IEvidenceStorage storage = null)
            {
                Assert.That(CombatEvidenceRuntime.Instance, Is.Null);
                Assert.That(CombatEvidence.Sink, Is.Null);
                root = new GameObject("inactive-evidence-shutdown-test"); root.SetActive(false);
                Runtime = root.AddComponent<CombatEvidenceRuntime>(); Runtime.enabled = false;
                var options = EvidenceStoreOptions.ForProfile(diagnostic ? EvidenceProfile.Diagnostic : EvidenceProfile.Standard, true);
                options.DeferObservationWindows = true;
                if (storage != null) options.Storage = storage;
                Store = new CombatEvidenceStore(DirectoryPath, options);
                Field("store").SetValue(Runtime, Store); Field("capture").SetValue(Runtime, "capture");
                Field("mainThread").SetValue(Runtime, Thread.CurrentThread.ManagedThreadId);
                // Keep Stamp.RefreshContext aligned with an already established boot context.
                Field("run").SetValue(Runtime, "boot"); Field("round").SetValue(Runtime, 0u);
                Field("contextRun").SetValue(Runtime, "boot"); Field("contextRound").SetValue(Runtime, 0u);
                Field("context").SetValue(Runtime, "boot/0");
                Store.QueueObservation.MarkPhase(EvidenceQueuePhase.Load, EvidenceQueueObservation.Now);
                CombatEvidence.Sink = Runtime;
            }
            internal void BlockWriter()
            {
                Assert.That(Store.Schedule(512, () => { entered.Set(); if (!release.Wait(5000)) Interlocked.Exchange(ref gateExpired, 1); }), Is.True);
                Assert.That(entered.Wait(5000), Is.True);
            }
            internal bool WantsToQuit() => (bool)Method("WantsToQuit").Invoke(Runtime, null);
            internal bool Poll(double now) => (bool)Method("PollShutdown").Invoke(Runtime, new object[] { now });
            internal void Fallback() => Method("Shutdown").Invoke(Runtime, null);
            internal string Export() => (string)Method("ExportWriterObservation", true).Invoke(null, new object[] { Store, "capture", true });
            internal JObject Status() => JObject.Parse(File.ReadAllText(StatusPath));
            internal JObject ShutdownStatus() => JObject.Parse(File.ReadAllText(ShutdownStatusPath));
            internal void Acknowledge() => Method("AcknowledgeIncompleteShutdown").Invoke(Runtime, null);
            internal void Abandon(double now) => Method("ConfirmAbandonShutdown").Invoke(Runtime, new object[] { now });
            internal void FinishDiagnostic()
            {
                Poll(Started + 62);
                Assert.That(SpinWait.SpinUntil(() => Field("diagnosticResult").GetValue(Runtime) != null, 5000), Is.True);
                JoinFinalizers();
            }
            internal void JoinFinalizers()
            {
                foreach (string name in new[] { "diagnosticFinalizer", "abandonmentWriter" })
                    if (Field(name).GetValue(Runtime) is Thread thread) Assert.That(thread.Join(5000), Is.True);
            }
            internal void ReleaseAndJoin()
            {
                release.Set(); Assert.That(Store.WaitForClose(5000), Is.True);
                Assert.That(Volatile.Read(ref gateExpired), Is.Zero, "The gate watchdog must not supply the test's release.");
            }
            internal void AssertSingleStopAndCompleteCoverage()
            {
                var records = Directory.GetFiles(DirectoryPath, "events-*.jsonl", SearchOption.AllDirectories)
                    .SelectMany(File.ReadLines).SelectMany(EvidenceBlocks.Decode).ToArray();
                Assert.That(records.Length, Is.EqualTo(1));
                Assert.That(records[0].stage, Is.EqualTo("process.stop"));
                var coverage = JObject.Parse(File.ReadAllText(Path.Combine(DirectoryPath, "boot", "0", "sources", "capture", "coverage.json")));
                Assert.That((bool)coverage["complete"], Is.True);
                Assert.That((bool)coverage["tailUnknown"], Is.False);
                Assert.That(Store.PendingBytes, Is.Zero);
            }
            public void Dispose()
            {
                release.Set(); CombatEvidence.Sink = null; Field("shuttingDown").SetValue(Runtime, true);
                Store.Dispose(); bool joined = Store.WaitForClose(5000);
                JoinFinalizers();
                if (joined) Store.QueueObservation?.ReleaseAfterStop(true, true);
                Field("quitAllowed").SetValue(Runtime, true);
                UnityEngine.Object.DestroyImmediate(root);
                Cursor.lockState = previousCursorLock; Cursor.visible = previousCursorVisible;
                if (joined) { entered.Dispose(); release.Dispose(); Directory.Delete(DirectoryPath, true); }
                Assert.That(joined, Is.True);
            }
        }

        private sealed class FailingStorage : IEvidenceStorage
        {
            private readonly FileEvidenceStorage actual = new();
            private readonly bool failFlush;
            internal FailingStorage(bool failFlush) { this.failFlush = failFlush; }
            public Stream OpenAppend(string path) => failFlush ? actual.OpenAppend(path) : throw new IOException("InjectedShutdownWriteFailure");
            public void Flush(Stream stream, bool durable) => throw new IOException("InjectedShutdownFlushFailure");
        }
    }
}
