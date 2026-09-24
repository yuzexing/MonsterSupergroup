using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatEvidenceServiceObservationTests
    {
        [Test] public void ServiceEvidenceHasDirectWorkIdentityAndBoundedNestedStageSurface()
        {
            var observer = typeof(EvidenceQueueObservation);
            foreach (string method in new[] { "PublishDequeuedWork", "BeginService", "BindServiceIdentity",
                "BeginStage", "EndStage", "RecordFlushedBlock", "RecordIoRetry", "EndService" })
                Assert.That(observer.GetMethod(method, BindingFlags.Instance | BindingFlags.Public), Is.Not.Null,
                    "The existing queue timeline cannot directly associate a long service with its work identity and internal stages: " + method);
            var identity = observer.Assembly.GetType("MonsterSupergroup.NetworkCombat.Diagnostics.EvidenceServiceIdentity");
            Assert.That(identity, Is.Not.Null);
            foreach (string field in new[] { "runId", "captureId", "round", "firstSequence", "lastSequence", "engine", "operation" })
                Assert.That(identity.GetField(field), Is.Not.Null, field);
        }

        [Test] public void ServiceEvidenceRetainsFixedCapacityWithoutIncreasingTheExistingReservation()
        {
            var observer = typeof(EvidenceQueueObservation);
            var capacity = observer.GetField("ServiceDetailCapacity", BindingFlags.Static | BindingFlags.Public);
            Assert.That(capacity, Is.Not.Null, "First, slowest and first-rejection-linked work details require a fixed retained capacity.");
            Assert.That((int)capacity.GetRawConstantValue(), Is.EqualTo(28));
            Assert.That(EvidenceQueueObservation.ReservedBytes, Is.EqualTo(512 << 10));
            Assert.That(observer.GetProperty("ServiceStorageBytes", BindingFlags.Static | BindingFlags.Public), Is.Not.Null,
                "The concrete stage, identity and scratch layout must be accounted inside the existing reservation.");
        }

        [Test] public void ServiceCpuCounterReportsAvailabilityAndResolutionInsteadOfAssumingZero()
        {
            var counter = typeof(EvidenceQueueObservation).Assembly.GetType(
                "MonsterSupergroup.NetworkCombat.Diagnostics.EvidenceServiceCpuCounter");
            Assert.That(counter, Is.Not.Null, "Wall time alone cannot distinguish CPU work from waits; unavailable CPU timing must stay explicit.");
            Assert.That(counter.GetMethod("Probe", BindingFlags.Static | BindingFlags.Public), Is.Not.Null);
            Assert.That(counter.GetMethod("Read", BindingFlags.Static | BindingFlags.Public), Is.Not.Null);
        }

        [Test] public void ConcreteLayoutAndAllBoundedIdentityBodiesFitTheExistingReservation()
        {
            var type = typeof(EvidenceQueueObservation);
            long windows = (long)(Marshal.SizeOf(type.GetNestedType("ProducerWindow", BindingFlags.NonPublic)) +
                Marshal.SizeOf(type.GetNestedType("ConsumerWindow", BindingFlags.NonPublic))) * EvidenceQueueObservation.WindowCount;
            Assert.That(Enum.GetValues(typeof(EvidenceServiceStage)).Length, Is.EqualTo(EvidenceQueueObservation.ServiceStageCount));
            Assert.That(EvidenceQueueObservation.ServiceStageCount, Is.LessThanOrEqualTo(64));
            Assert.That(windows + (64 << 10) + EvidenceQueueObservation.ServiceStorageBytes,
                Is.LessThanOrEqualTo(EvidenceQueueObservation.ReservedBytes));
            TestContext.WriteLine("Queue windows={0}; old metadata allowance={1}; service layout={2}; total={3}; reservation={4}",
                windows, 64 << 10, EvidenceQueueObservation.ServiceStorageBytes,
                windows + (64 << 10) + EvidenceQueueObservation.ServiceStorageBytes, EvidenceQueueObservation.ReservedBytes);
        }

        [Test] public void NestedStagesKeepExclusiveTimeAndUnclassifiedWallResidual()
        {
            var observer = Create(); Begin(observer, 1, 100);
            observer.BeginStage(EvidenceServiceStage.Append, 110);
            observer.BeginStage(EvidenceServiceStage.JsonEncode, 120);
            observer.EndStage(EvidenceServiceStage.JsonEncode, 140);
            observer.BeginStage(EvidenceServiceStage.AtomicWrite, 145);
            observer.EndStage(EvidenceServiceStage.AtomicWrite, 175);
            observer.EndStage(EvidenceServiceStage.Append, 180);
            Finish(observer, 200);
            var result = Export(observer); var work = Work(result, 1);
            Assert.That((long)Stage(work, "Append")["inclusiveTicks"], Is.EqualTo(70));
            Assert.That((long)Stage(work, "Append")["exclusiveTicks"], Is.EqualTo(20));
            Assert.That((long)Stage(work, "JsonEncode")["exclusiveTicks"], Is.EqualTo(20));
            Assert.That((long)work["wallResidualTicks"], Is.EqualTo(30));
            Assert.That((long)work["wallTicks"], Is.EqualTo(100));
            Assert.That((bool)result["serviceDetails"]["complete"], Is.True);
        }

        [Test] public void ZeroDurationStageIsPresentAndStackFailureNeverClaimsComplete()
        {
            var observer = Create(); Begin(observer, 1, 100);
            observer.BeginStage(EvidenceServiceStage.AtomicFlush, 110); observer.EndStage(EvidenceServiceStage.AtomicFlush, 110);
            for (int i = 0; i < EvidenceQueueObservation.ServiceStackDepth + 1; i++) observer.BeginStage(EvidenceServiceStage.Append, 120);
            for (int i = 0; i < EvidenceQueueObservation.ServiceStackDepth + 1; i++) observer.EndStage(EvidenceServiceStage.Append, 130);
            Finish(observer, 200); var result = Export(observer); var work = Work(result, 1);
            Assert.That((long)Stage(work, "AtomicFlush")["inclusiveTicks"], Is.Zero);
            Assert.That((bool)work["stagesComplete"], Is.False);
            Assert.That(work["wallResidualTicks"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That((bool)result["serviceDetails"]["complete"], Is.False);
        }

        [Test] public void FirstAndSlowestRetainDirectIdentityAcrossMoreThanSixteenEngines()
        {
            var observer = Create();
            for (int i = 1; i <= 80; i++) { Begin(observer, i, i * 1000, "gateway." + i); Finish(observer, i * 1000 + i); }
            var result = Export(observer); var items = result["serviceDetails"]["workItems"];
            CollectionAssert.AreEqual(Enumerable.Range(1, 8).Select(i => (long)i),
                items.Where(item => (bool)item["retention"]["first"]).Select(item => (long)item["workId"]));
            CollectionAssert.AreEqual(Enumerable.Range(65, 16).Select(i => (long)i),
                items.Where(item => (bool)item["retention"]["slowest"]).Select(item => (long)item["workId"]));
            Assert.That((string)Work(result, 80)["engine"], Is.EqualTo("gateway.80"));
            Assert.That((long)result["serviceDetails"]["overflowEvents"], Is.Zero);
            Assert.That((long)result["serviceDetails"]["unretainedWorkItems"], Is.EqualTo(56));
            Assert.That((bool)result["serviceDetails"]["complete"], Is.True);
        }

        [Test] public void FirstRejectionLinksOnlyActiveAndPreviousEvenWhenNeitherIsSlowest()
        {
            var observer = Create();
            for (int i = 1; i <= 40; i++) { Begin(observer, i, i * 1000); Finish(observer, i * 1000 + 1000 - i); }
            Begin(observer, 41, 41000); Reject(observer, 41001); Finish(observer, 41002);
            for (int i = 42; i <= 80; i++) { Begin(observer, i, i * 1000); Finish(observer, i * 1000 + 1); }
            var result = Export(observer); var details = result["serviceDetails"];
            Assert.That((long)details["firstRejectionWork"]["activeWorkId"], Is.EqualTo(41));
            CollectionAssert.AreEqual(new[] { 40L }, details["firstRejectionWork"]["precedingWorkIds"].Select(item => (long)item));
            CollectionAssert.AreEqual(new[] { 40L, 41L }, details["workItems"].Where(item => (bool)item["retention"]["firstRejectionLinked"]).Select(item => (long)item["workId"]));
            Assert.That((bool)details["firstRejectionWork"]["linksComplete"], Is.True);
            Assert.That((bool)details["complete"], Is.True);
        }

        [Test] public void RejectionOutsideServiceHasNoInventedActiveWork()
        {
            var observer = Create();
            for (int i = 1; i <= 30; i++) { Begin(observer, i, i * 1000); Finish(observer, i * 1000 + 1000 - i); }
            Reject(observer, 31000); var result = Export(observer); var details = result["serviceDetails"];
            Assert.That(details["firstRejectionWork"]["activeWorkId"].Type, Is.EqualTo(JTokenType.Null));
            CollectionAssert.AreEqual(new[] { 30L, 29L }, details["firstRejectionWork"]["precedingWorkIds"].Select(item => (long)item));
            CollectionAssert.AreEqual(new[] { 29L, 30L }, details["workItems"].Where(item => (bool)item["retention"]["firstRejectionLinked"]).Select(item => (long)item["workId"]));
            Assert.That(details["workItems"].Any(item => (long)item["workId"] == 28), Is.False);
        }

        [Test] public void ARejectionBeforeAnyWorkHasNoWorkLinks()
        {
            var observer = Create(); Reject(observer, 1); var details = Export(observer)["serviceDetails"];
            Assert.That(details["firstRejectionWork"]["activeWorkId"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That(details["firstRejectionWork"]["precedingWorkIds"].Count(), Is.Zero);
            Assert.That((bool)details["firstRejectionWork"]["linksComplete"], Is.True);
        }

        [Test] public void FlushedRangesDescribeActualBlocksAndTheirSourcesInsteadOfTheTrigger()
        {
            var observer = Create(); Begin(observer, 99, 100);
            observer.RecordFlushedBlockFromDirectory(@"F:\a-very-long-root\prior-run\6\sources\prior-cap", 10, 17, 8, EvidenceServiceBlockKind.Advance);
            observer.RecordFlushedBlock("run", 7, "capture", 99, 99, 1, EvidenceServiceBlockKind.JsonLine);
            Finish(observer, 200); var work = Work(Export(observer), 1); var blocks = work["flushedBlocks"];
            Assert.That((ulong)work["firstSequence"], Is.EqualTo(99));
            Assert.That((ulong)blocks[0]["firstSequence"], Is.EqualTo(10));
            Assert.That((ulong)blocks[0]["lastSequence"], Is.EqualTo(17));
            Assert.That((int)blocks[0]["recordCount"], Is.EqualTo(8));
            Assert.That((string)blocks[0]["source"]["runId"], Is.EqualTo("prior-run"));
            Assert.That((uint)blocks[0]["source"]["round"], Is.EqualTo(6));
            Assert.That((string)blocks[1]["kind"], Is.EqualTo("JsonLine"));
            Assert.That((bool)work["flushedBlocksComplete"], Is.True);
        }

        [Test] public void ExtraFlushRangesAndOversizedIdentityAreExplicitlyIncomplete()
        {
            var observer = Create(); Begin(observer, 1, 100, new string('e', EvidenceQueueObservation.ServiceIdentityLength + 1));
            for (int i = 1; i <= 3; i++) observer.RecordFlushedBlock("run", 7, "capture", (ulong)i, (ulong)i, 1, EvidenceServiceBlockKind.JsonLine);
            Finish(observer, 200); var result = Export(observer); var work = Work(result, 1);
            Assert.That(work["engine"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That((bool)work["identityComplete"], Is.False);
            Assert.That((bool)work["flushedBlocksComplete"], Is.False);
            Assert.That((int)work["flushedBlockCount"], Is.EqualTo(3));
            Assert.That(work["flushedBlocks"].Count(), Is.EqualTo(2));
            Assert.That((bool)result["serviceDetails"]["complete"], Is.False);
        }

        [Test] public void SourceCapacityCannotSilentlyAliasNewSources()
        {
            var observer = Create();
            for (int i = 1; i <= 9; i++)
            {
                Begin(observer, i, i * 1000, "gateway", "run" + i); Finish(observer, i * 1000 + i);
            }
            var result = Export(observer); var last = Work(result, 9);
            Assert.That(last["source"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That((bool)last["identityComplete"], Is.False);
            Assert.That((bool)result["serviceDetails"]["complete"], Is.False);
        }

        [Test] public void FinalIoFailureIsNotCountedAsAnotherRetry()
        {
            var observer = Create(); Begin(observer, 1, 100);
            for (int attempt = 0; attempt < 4; attempt++) observer.RecordIoRetry(unchecked((int)0x80070020), attempt, attempt < 3);
            Finish(observer, 200); var work = Work(Export(observer), 1);
            Assert.That((int)work["ioFailureCount"], Is.EqualTo(4));
            Assert.That((int)work["retryCount"], Is.EqualTo(3));
            Assert.That((int)work["lastRetryAttempt"], Is.EqualTo(3));
            Assert.That((int)work["lastRetryHResult"], Is.EqualTo(unchecked((int)0x80070020)));
        }

        [Test] public void BackgroundContextsDoNotConsumeWorkOrdinalsOrLeakIntoFollowingWork()
        {
            var observer = Create(); observer.BeginBackground(EvidenceServiceContext.Startup, 1);
            observer.BeginStage(EvidenceServiceStage.SourceRecover, 2); observer.EndStage(EvidenceServiceStage.SourceRecover, 5);
            observer.EndBackground(10); Begin(observer, 1, 100); Finish(observer, 200);
            observer.BeginBackground(EvidenceServiceContext.Close, 201);
            observer.BeginStage(EvidenceServiceStage.DurableFlush, 202); observer.EndStage(EvidenceServiceStage.DurableFlush, 210);
            observer.EndBackground(220); var result = Export(observer); var details = result["serviceDetails"];
            Assert.That(details["workItems"].Count(), Is.EqualTo(1));
            Assert.That(Work(result, 1)["stages"].Count(), Is.Zero);
            Assert.That(details["background"].Count(), Is.EqualTo(2));
            Assert.That((string)details["background"][0]["context"], Is.EqualTo("Startup"));
            Assert.That((long)details["background"][1]["wallTicks"], Is.EqualTo(19));
        }

        [Test] public void ScopeUsesOnlyTheActiveWriterAndUnwindsExceptions()
        {
            var observer = Create(); long start = EvidenceQueueObservation.Now; Begin(observer, 1, start);
            var other = new Thread(() => { using var ignored = EvidenceServiceTiming.Measure(EvidenceServiceStage.AtomicWrite); });
            other.Start(); Assert.That(other.Join(2000), Is.True);
            Assert.Throws<InvalidOperationException>(() => { using var scope = EvidenceServiceTiming.Measure(EvidenceServiceStage.RecordJson); throw new InvalidOperationException(); });
            Finish(observer, EvidenceQueueObservation.Now); using (EvidenceServiceTiming.Measure(EvidenceServiceStage.AtomicFlush)) { }
            var work = Work(Export(observer), 1);
            Assert.That(work["stages"].Count(), Is.EqualTo(1));
            Assert.That((string)work["stages"][0]["stage"], Is.EqualTo("RecordJson"));
            Assert.That((bool)work["stagesComplete"], Is.True);
        }

        [Test] public void ReleasedObservationDropsReferencesAndFurtherServiceCallsAreSafe()
        {
            var memory = new DiagnosticMemoryBudget(); Assert.That(EvidenceQueueObservation.TryCreate(memory, true, out var observer, 1), Is.True);
            Begin(observer, 1, 100); Finish(observer, 200);
            Assert.That(observer.ReleaseAfterStop(true, false), Is.False);
            Assert.That(memory.Used, Is.EqualTo(EvidenceQueueObservation.ReservedBytes));
            Assert.That(observer.ReleaseAfterStop(true, true), Is.True);
            Assert.That(memory.Used, Is.Zero);
            foreach (string name in new[] { "serviceRecords", "serviceStages", "serviceFlushRanges", "serviceSources", "serviceSourceCharacters", "serviceStack", "serviceBackgrounds", "serviceBackgroundStages", "serviceCpuProbe" })
                Assert.That(typeof(EvidenceQueueObservation).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(observer), Is.Null, name);
            Assert.DoesNotThrow(() => { observer.PublishDequeuedWork(1, 512); observer.BeginService(2); observer.BindServiceIdentity(default);
                observer.BeginStage(EvidenceServiceStage.Append, 3); observer.EndStage(EvidenceServiceStage.Append, 4);
                observer.RecordIoRetry(1); observer.RecordFlushedBlockFromDirectory(null, 1, 1, 1, EvidenceServiceBlockKind.JsonLine); observer.EndService(5); });
        }

        [Test] public void CpuClassificationPreservesUnknownAndBelowResolutionInsteadOfZero()
        {
            var constructor = typeof(EvidenceServiceCpuProbe).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var unavailable = (EvidenceServiceCpuProbe)constructor.Invoke(new object[] { false, false, "test", 0L, 0L, 0L, 0L, 0L, 0, 0, 0, 0 });
            var measured = (EvidenceServiceCpuProbe)constructor.Invoke(new object[] { true, true, "test", 100L, 1000L, 0L, 1L, 100L, 2, 3, 7, 8 });
            Assert.That(EvidenceServiceCpuCounter.Classify(unavailable, true, true, true, 10, 10), Is.EqualTo(EvidenceServiceCpuStatus.Unverified));
            Assert.That(EvidenceServiceCpuCounter.Classify(measured, true, true, true, 10, 10), Is.EqualTo(EvidenceServiceCpuStatus.BelowObservedResolution));
            Assert.That(EvidenceServiceCpuCounter.Classify(measured, true, false, true, 10, 200), Is.EqualTo(EvidenceServiceCpuStatus.ReadFailed));
            Assert.That(EvidenceServiceCpuCounter.Classify(measured, true, true, false, 10, 200), Is.EqualTo(EvidenceServiceCpuStatus.ThreadMismatch));
            Assert.That(EvidenceServiceCpuCounter.Classify(measured, true, true, true, 200, 10), Is.EqualTo(EvidenceServiceCpuStatus.CounterReversed));
            Assert.That(EvidenceServiceCpuCounter.Classify(measured, true, true, true, 10, 200), Is.EqualTo(EvidenceServiceCpuStatus.Measured));
        }

        [Test] public void ShortWallWorkCrossingOneCpuClockTickIsStillBelowResolution()
        {
            var constructor = typeof(EvidenceServiceCpuProbe).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var measured = (EvidenceServiceCpuProbe)constructor.Invoke(new object[] { true, true, "test", 156250L, 625000L, 0L, 1L, 100L, 4, 60, 7, 8 });
            long oneMillisecond = Math.Max(1, System.Diagnostics.Stopwatch.Frequency / 1000);
            Assert.That(EvidenceServiceCpuCounter.Classify(measured, true, true, true, 0, 156250, oneMillisecond),
                Is.EqualTo(EvidenceServiceCpuStatus.BelowObservedResolution));
            Assert.That(EvidenceServiceCpuCounter.Classify(measured, true, true, true, 0, 156250, oneMillisecond * 20),
                Is.EqualTo(EvidenceServiceCpuStatus.Measured));
        }

        [Test] public void NativeCpuReadPreservesBothCumulativeComponentsWithoutAdditionalReads()
        {
            var sample = EvidenceServiceCpuCounter.Read();
            Assert.That(sample.threadId, Is.EqualTo(Environment.CurrentManagedThreadId));
            if (sample.available)
            {
                Assert.That(sample.user100ns, Is.GreaterThanOrEqualTo(0)); Assert.That(sample.kernel100ns, Is.GreaterThanOrEqualTo(0));
                Assert.That(sample.total100ns, Is.EqualTo(sample.user100ns + sample.kernel100ns));
            }
            else Assert.That(sample.total100ns, Is.Zero, "Zero is only an unavailable raw counter, never a measured CPU result.");
        }

        [Test] public void ProducerRejectionReadsPublishedIdBeforeWriterHasWrittenTheNewDetail()
        {
            var observer = Create(); var gate = new object();
            using var published = new ManualResetEventSlim(); using var proceed = new ManualResetEventSlim();
            Exception workerFailure = null;
            for (int i = 1; i <= 2; i++)
            {
                observer.Attempt(EvidenceQueueEntry.TryWrite, i * 100); observer.Accepted(EvidenceQueueEntry.TryWrite, i * 100);
                observer.Enqueued(512, i * 512, i * 100);
            }
            var worker = new Thread(() => {
                try
                {
                    for (int i = 1; i <= 2; i++)
                    {
                        lock (gate) observer.PublishDequeuedWork(i * 100, 512);
                        if (i == 2) { published.Set(); if (!proceed.Wait(2000)) throw new TimeoutException(); }
                        observer.Dequeued(i * 100); observer.BeginService(i * 100);
                        observer.BindServiceIdentity(new EvidenceServiceIdentity { kind = EvidenceServiceWorkKind.Record,
                            runId = "run", round = 7, captureId = "capture", firstSequence = (ulong)i, lastSequence = (ulong)i,
                            logicalCount = 1, phase = -1, engine = "gateway." + i, stage = "replay.engine_checkpoint" });
                        observer.EndService(i * 100 + 50); observer.Completed(512, i * 100 + 50);
                        lock (gate) { observer.Pending((2 - i) * 512, i * 100 + 50); observer.ConsumerPending((2 - i) * 512, i * 100 + 50); }
                    }
                }
                catch (Exception error) { workerFailure = error; published.Set(); }
            }) { IsBackground = true };
            worker.Start();
            try
            {
                Assert.That(published.Wait(2000), Is.True); Assert.That(workerFailure, Is.Null);
                // The writer published work 2, but has not started its context or bound any identity yet.
                lock (gate) Reject(observer, 201);
                proceed.Set(); Assert.That(worker.Join(2000), Is.True); Assert.That(workerFailure, Is.Null);
                var result = Export(observer); var link = result["serviceDetails"]["firstRejectionWork"];
                Assert.That((long)link["activeWorkId"], Is.EqualTo(2));
                CollectionAssert.AreEqual(new[] { 1L }, link["precedingWorkIds"].Select(item => (long)item));
                Assert.That((bool)link["linksComplete"], Is.True);
                Assert.That((string)Work(result, 2)["engine"], Is.EqualTo("gateway.2"));
                Assert.That((bool)result["serviceDetails"]["complete"], Is.True);
            }
            finally { proceed.Set(); if (worker.Join(2000)) observer.ReleaseAfterStop(true, true); }
        }

        private static EvidenceQueueObservation Create()
        {
            Assert.That(EvidenceQueueObservation.TryCreate(new DiagnosticMemoryBudget(), true, out var observer, 1), Is.True); return observer;
        }
        private static void Begin(EvidenceQueueObservation observer, int sequence, long start, string engine = "gateway.1", string run = "run")
        {
            observer.Attempt(EvidenceQueueEntry.TryWrite, start); observer.Accepted(EvidenceQueueEntry.TryWrite, start);
            observer.Enqueued(512, 512, start); observer.PublishDequeuedWork(start, 512); observer.Dequeued(start); observer.BeginService(start);
            observer.BindServiceIdentity(new EvidenceServiceIdentity { kind = EvidenceServiceWorkKind.Record, runId = run, round = 7, captureId = "capture",
                firstSequence = (ulong)sequence, lastSequence = (ulong)sequence, logicalCount = 1, phase = -1, engine = engine,
                operation = "Checkpoint", stage = "replay.engine_checkpoint" });
        }
        private static void Finish(EvidenceQueueObservation observer, long end)
        { observer.EndService(end); observer.Completed(512, end); observer.Pending(0, end); observer.ConsumerPending(0, end); }
        private static void Reject(EvidenceQueueObservation observer, long ticks)
        { observer.Attempt(EvidenceQueueEntry.TryWriteAdvance, ticks); observer.Rejected(EvidenceQueueEntry.TryWriteAdvance, EvidenceQueueGuard.QueueLimit, default, ticks); }
        private static JToken Work(JObject result, long id) => result["serviceDetails"]["workItems"].Single(item => (long)item["workId"] == id);
        private static JToken Stage(JToken work, string name) => work["stages"].Single(stage => (string)stage["stage"] == name);
        private static JObject Export(EvidenceQueueObservation observer)
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            try { Assert.That(observer.StopAndExport(path, true, true), Is.True); return JObject.Parse(File.ReadAllText(path)); }
            finally { observer.ReleaseAfterStop(true, true); if (File.Exists(path)) File.Delete(path); }
        }
    }
}
