using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatEvidencePartialObservationTests
    {
        [Test] public void StalledServiceRetainsPartialProgressAndCanLaterExportCompleteObservation()
        {
            string root = TemporaryDirectory();
            var memory = new DiagnosticMemoryBudget(EvidenceQueueObservation.ReservedBytes);
            Assert.That(EvidenceQueueObservation.TryCreate(memory, true, out var observer, 1000), Is.True);
            using var ready = new ManualResetEventSlim(); using var resume = new ManualResetEventSlim();
            Exception workerFailure = null;
            observer.Attempt(EvidenceQueueEntry.TryWrite, 1001); observer.Accepted(EvidenceQueueEntry.TryWrite, 1001);
            observer.Enqueued(2048, 2048, 1002);
            var worker = new Thread(() => {
                try
                {
                    observer.PublishDequeuedWork(1002, 2048); observer.BeginService(1100); observer.Dequeued(1100);
                    observer.BindServiceIdentity(new EvidenceServiceIdentity { kind = EvidenceServiceWorkKind.Record,
                        runId = "run", captureId = "capture", round = 1, firstSequence = 29, lastSequence = 29,
                        logicalCount = 1, engine = "status-32", stage = "replay.engine_checkpoint" });
                    observer.BeginStage(EvidenceServiceStage.Append, 1110);
                    observer.BeginStage(EvidenceServiceStage.PayloadFlush, 1120); observer.EndStage(EvidenceServiceStage.PayloadFlush, 1220);
                    observer.BeginStage(EvidenceServiceStage.AtomicFlush, 1230);
                    ready.Set(); resume.Wait();
                    observer.EndStage(EvidenceServiceStage.AtomicFlush, 1330); observer.EndStage(EvidenceServiceStage.Append, 1340);
                    observer.EndService(1350); observer.Span(EvidenceQueueStage.Service, 1100, 1350);
                    observer.Completed(2048, 1350); observer.Pending(0, 1350); observer.ConsumerPending(0, 1350);
                }
                catch (Exception failure) { workerFailure = failure; ready.Set(); }
            });
            worker.IsBackground = true; worker.Start();
            try
            {
                Assert.That(ready.Wait(5000), Is.True); Assert.That(workerFailure, Is.Null);
                observer.Attempt(EvidenceQueueEntry.TryWriteAdvance, 1240);
                var rejection = new EvidenceQueueRejection { runId = "run", captureId = "capture", round = 1,
                    sequence = 30, requestedBytes = 65536, pendingBytes = 2048, effectiveQueueLimit = 4096, stage = "replay.input" };
                observer.Rejected(EvidenceQueueEntry.TryWriteAdvance, EvidenceQueueGuard.QueueLimit, in rejection, 1240);
                string partial = Path.Combine(root, "partial.json");
                Assert.That(observer.ExportPartial(partial, true), Is.True);
                var result = JObject.Parse(File.ReadAllText(partial));
                Assert.That((bool)result["writerJoined"], Is.False);
                Assert.That((bool)result["observationComplete"], Is.False);
                Assert.That(result["countsBalanced"].Type, Is.EqualTo(JTokenType.Null));
                Assert.That((bool)result["snapshotAtomic"], Is.False);
                Assert.That((long)result["producerTotals"]["pendingEndBytes"], Is.EqualTo(2048));
                Assert.That((long)result["consumerTotals"]["workDequeued"], Is.EqualTo(1));
                Assert.That((long)result["consumerTotals"]["workCompleted"], Is.Zero);
                Assert.That((string)result["firstRejection"]["guard"], Is.EqualTo("QueueLimit"));
                Assert.That((long)result["firstRejection"]["context"]["requestedBytes"], Is.EqualTo(65536));
                Assert.That((bool)result["currentWorkAvailable"], Is.True);
                Assert.That((bool)result["currentWork"]["active"], Is.True);
                Assert.That((ulong)result["currentWork"]["identity"]["firstSequence"], Is.EqualTo(29));
                Assert.That((string)result["currentStage"], Is.EqualTo("AtomicFlush"));
                var flush = result["serviceStages"].Single(s => (string)s["stage"] == "PayloadFlush");
                Assert.That((long)flush["inclusiveTicks"], Is.EqualTo(100));
                Assert.That((long)flush["exclusiveTicks"], Is.EqualTo(100));
                Assert.That(result["queueStages"].Count(), Is.EqualTo(Enum.GetValues(typeof(EvidenceQueueStage)).Length));
                Assert.That(memory.Used, Is.EqualTo(EvidenceQueueObservation.ReservedBytes));
                resume.Set(); Assert.That(worker.Join(5000), Is.True); Assert.That(workerFailure, Is.Null);
                var mirror = (long[])PrivateField(observer, "partialEntryCounts");
                string complete = Path.Combine(root, "complete.json");
                Assert.That(observer.StopAndExport(complete, true, true), Is.True);
                Assert.That((bool)JObject.Parse(File.ReadAllText(complete))["countsBalanced"], Is.True);
                Assert.That(memory.Used, Is.Zero);
                Assert.That(File.Exists(partial), Is.True);
                foreach (string field in new[] { "partialEntryCounts", "partialGuardCounts", "partialQueueStageTicks",
                    "partialServiceInclusive", "partialServiceExclusive", "partialFirstRejection" })
                    Assert.That(PrivateField(observer, field), Is.Null, field);
                object work = PrivateField(observer, "partialWork");
                var identity = (EvidenceServiceIdentity)work.GetType().GetField("identity").GetValue(work);
                Assert.That(identity.runId, Is.Null); Assert.That(identity.captureId, Is.Null);
                Assert.That(identity.engine, Is.Null); Assert.That(identity.operation, Is.Null); Assert.That(identity.stage, Is.Null);
                // An exporter that captured the array before release can still read it safely after detachment.
                var read = typeof(EvidenceQueueObservation).GetMethod("ReadPartialCounters", BindingFlags.Static | BindingFlags.NonPublic);
                var detached = (long[])read.Invoke(null, new object[] { mirror });
                Assert.That(detached[0], Is.EqualTo(1)); Assert.That(detached[1], Is.EqualTo(1));
                Assert.That(observer.ExportPartial(Path.Combine(root, "after-release.json"), true), Is.False);
            }
            finally
            {
                resume.Set(); worker.Join(5000); observer.ReleaseAfterStop(true, true);
                Directory.Delete(root, true);
            }
        }

        [Test] public void PartialExportDoesNotWaitForTheCurrentWorkMetadataLock()
        {
            string root = TemporaryDirectory();
            Assert.That(EvidenceQueueObservation.TryCreate(new DiagnosticMemoryBudget(), true, out var observer, 1000), Is.True);
            object gate = typeof(EvidenceQueueObservation).GetField("partialProgressGate", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(observer);
            using var held = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var exported = new ManualResetEventSlim();
            Exception exportFailure = null; bool result = false;
            var holder = new Thread(() => { lock (gate) { held.Set(); release.Wait(); } }) { IsBackground = true };
            string path = Path.Combine(root, "partial.json");
            var exporter = new Thread(() => { try { result = observer.ExportPartial(path, true); } catch (Exception error) { exportFailure = error; } finally { exported.Set(); } }) { IsBackground = true };
            holder.Start();
            try
            {
                Assert.That(held.Wait(5000), Is.True); exporter.Start();
                bool finishedWhileLockHeld = exported.Wait(2000);
                release.Set(); Assert.That(holder.Join(5000), Is.True); Assert.That(exporter.Join(5000), Is.True);
                Assert.That(finishedWhileLockHeld, Is.True, "Partial export must not wait for the writer metadata lock.");
                Assert.That(exportFailure, Is.Null); Assert.That(result, Is.True);
                var snapshot = JObject.Parse(File.ReadAllText(path));
                Assert.That((bool)snapshot["currentWorkAvailable"], Is.False);
                Assert.That(snapshot["currentWork"].Type, Is.EqualTo(JTokenType.Null));
            }
            finally { release.Set(); holder.Join(5000); if (exporter.IsAlive) exporter.Join(5000); observer.ReleaseAfterStop(true, true); Directory.Delete(root, true); }
        }

        [Test] public void InterruptedPublicationLeavesNoFinalFileAndPartialCanRetryTheSamePath()
        {
            string root = TemporaryDirectory();
            var memory = new DiagnosticMemoryBudget();
            Assert.That(EvidenceQueueObservation.TryCreate(memory, true, out var observer, 1000), Is.True);
            try
            {
                string path = Path.Combine(root, "partial.json");
                var publish = typeof(EvidenceQueueObservation).GetMethod("PublishObservationFile", BindingFlags.Static | BindingFlags.NonPublic);
                Action<JsonTextWriter, JsonSerializer> interrupted = (writer, serializer) => {
                    writer.WriteStartObject(); writer.WritePropertyName("interrupted"); writer.WriteValue(true); writer.Flush();
                    throw new IOException("Injected failure after partial bytes reached the temporary file.");
                };
                var failure = Assert.Throws<TargetInvocationException>(() => publish.Invoke(null, new object[] { path, interrupted }));
                Assert.That(failure.InnerException, Is.TypeOf<IOException>());
                Assert.That(File.Exists(path), Is.False);
                Assert.That(memory.Used, Is.EqualTo(EvidenceQueueObservation.ReservedBytes));
                Assert.That(Directory.GetFiles(root, "*.tmp"), Is.Empty);
                Assert.That(observer.ExportPartial(path, true), Is.True);
                Assert.That((string)JObject.Parse(File.ReadAllText(path))["kind"], Is.EqualTo("PartialProgress"));
                Assert.That(memory.Used, Is.EqualTo(EvidenceQueueObservation.ReservedBytes));
                Assert.That(observer.StopAndExport(Path.Combine(root, "complete.json"), true, true), Is.True);
                Assert.That(memory.Used, Is.Zero);
            }
            finally { observer.ReleaseAfterStop(true, true); Directory.Delete(root, true); }
        }

        [Test] public void PartialDoesNotStopAnActiveMainObservationOrReleaseOnOutputFailure()
        {
            string root = TemporaryDirectory(); var memory = new DiagnosticMemoryBudget();
            Assert.That(EvidenceQueueObservation.TryCreate(memory, true, out var observer, 1000), Is.True);
            try
            {
                observer.BeginMainFrame(1, 1100, 1);
                string path = Path.Combine(root, "partial.json");
                Assert.That(observer.ExportPartial(path, false), Is.False);
                Assert.That(observer.ExportPartial(path, true), Is.True);
                string original = File.ReadAllText(path);
                Assert.Throws<IOException>(() => observer.ExportPartial(path, true));
                Assert.That(File.ReadAllText(path), Is.EqualTo(original));
                Assert.That(memory.Used, Is.EqualTo(EvidenceQueueObservation.ReservedBytes));
                observer.EndMainFrame(1200, 1);
                Assert.That(observer.StopAndExport(Path.Combine(root, "complete.json"), true, true), Is.True);
                Assert.That(memory.Used, Is.Zero);
            }
            finally { observer.EndMainFrame(1200, 1); observer.ReleaseAfterStop(true, true); Directory.Delete(root, true); }
        }

        [Test] public void PartialMirrorAndExistingDetailedObservationFitTheOriginalReservation()
        {
            Assert.That(EvidenceQueueObservation.ReservedBytes, Is.EqualTo(512 << 10));
            Assert.That(EvidenceQueueObservation.AccountedStorageBytes, Is.LessThanOrEqualTo(EvidenceQueueObservation.ReservedBytes));
            Assert.That(Enum.GetValues(typeof(EvidenceServiceStage)).Length, Is.EqualTo(EvidenceQueueObservation.ServiceStageCount));
            TestContext.WriteLine("Partial mirror allowance={0}; total accounted={1}; reservation={2}",
                EvidenceQueueObservation.PartialStorageBytes, EvidenceQueueObservation.AccountedStorageBytes, EvidenceQueueObservation.ReservedBytes);
        }

        private static string TemporaryDirectory()
        { string path = Path.Combine(Path.GetTempPath(), "combat-partial-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
        private static object PrivateField(EvidenceQueueObservation observer, string name) =>
            typeof(EvidenceQueueObservation).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(observer);
    }
}
