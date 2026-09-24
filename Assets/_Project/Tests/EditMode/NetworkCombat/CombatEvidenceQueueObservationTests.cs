using System;
using System.Diagnostics;
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
    public sealed class CombatEvidenceQueueObservationTests
    {
        [Test] public void BoundedQueueObservationSurfaceExists()
        {
            var observer = typeof(CombatEvidenceStore).Assembly.GetType(
                "MonsterSupergroup.NetworkCombat.Diagnostics.EvidenceQueueObservation");
            Assert.That(observer, Is.Not.Null, "Queue overload has no bounded producer/consumer timeline or exact first-guard observation.");
            Assert.That(observer.GetMethod("TryCreate"), Is.Not.Null);
            Assert.That(observer.GetMethod("StopAndExport"), Is.Not.Null);
        }

        [Test] public void DisabledOrUnfundedObservationNeverConsumesBudget()
        {
            var memory = new DiagnosticMemoryBudget(EvidenceQueueObservation.ReservedBytes - 1);
            Assert.That(EvidenceQueueObservation.TryCreate(memory, false, out var disabled), Is.False);
            Assert.That(disabled, Is.Null); Assert.That(memory.Used, Is.Zero);
            Assert.That(EvidenceQueueObservation.TryCreate(memory, true, out var unfunded), Is.False);
            Assert.That(unfunded, Is.Null); Assert.That(memory.Used, Is.Zero);
        }

        [Test] public void FixedReservationCoversBothNumericArraysWithRoomForBoundedIdentitySlotsAndHeaders()
        {
            var type = typeof(EvidenceQueueObservation);
            int producer = Marshal.SizeOf(type.GetNestedType("ProducerWindow", BindingFlags.NonPublic));
            int consumer = Marshal.SizeOf(type.GetNestedType("ConsumerWindow", BindingFlags.NonPublic));
            long arrays = (long)(producer + consumer) * EvidenceQueueObservation.WindowCount;
            // Conservative 64 KiB allowance for array/object headers, flags, phase slots and
            // thirteen first-rejection identities, each bounded to 100/100/128 characters.
            Assert.That(arrays + (64 << 10), Is.LessThanOrEqualTo(EvidenceQueueObservation.ReservedBytes));
        }

        [Test] public void ReservationIsHeldUntilBothWritersHaveStoppedAndReleasedExactlyOnce()
        {
            var memory = new DiagnosticMemoryBudget(EvidenceQueueObservation.ReservedBytes);
            Assert.That(EvidenceQueueObservation.TryCreate(memory, true, out var observer, 1), Is.True);
            Assert.That(memory.Used, Is.EqualTo(EvidenceQueueObservation.ReservedBytes));
            Assert.That(observer.ReleaseAfterStop(false, true), Is.False);
            Assert.That(observer.ReleaseAfterStop(true, false), Is.False);
            Assert.That(memory.Used, Is.EqualTo(EvidenceQueueObservation.ReservedBytes));
            Assert.That(observer.ReleaseAfterStop(true, true), Is.True);
            Assert.That(observer.ReleaseAfterStop(true, true), Is.False);
            Assert.That(memory.Used, Is.Zero);
            Assert.DoesNotThrow(() => {
                observer.Attempt(EvidenceQueueEntry.TryWrite, 2);
                observer.Rejected(EvidenceQueueEntry.TryWrite, EvidenceQueueGuard.Stopping, default, 2);
                observer.Pending(0, 2); observer.Completed(1, 2);
                observer.MarkPhase(EvidenceQueuePhase.Close, 2);
                observer.Span(EvidenceQueueStage.Service, 1, 2);
            });
            Assert.That(observer.ShouldCaptureFirst(EvidenceQueueEntry.TryWrite, EvidenceQueueGuard.Stopping), Is.False);
        }

        [Test] public void LogicalRecordsAreSeparateFromScheduleCallsAndBlockWorkItems()
        {
            var observer = Create();
            for (int i = 0; i < 128; i++)
            { observer.Attempt(EvidenceQueueEntry.TryWriteAdvance, Origin); observer.Accepted(EvidenceQueueEntry.TryWriteAdvance, Origin); }
            observer.Enqueued(65536, 65536, Origin);
            observer.Attempt(EvidenceQueueEntry.Schedule, Origin); observer.Accepted(EvidenceQueueEntry.Schedule, Origin);
            observer.Enqueued(512, 66048, Origin);
            observer.Dequeued(Origin); observer.Completed(65536, Origin);
            observer.Dequeued(Origin); observer.Completed(512, Origin);
            observer.Pending(0, Origin); observer.ConsumerPending(0, Origin);
            var result = Export(observer);
            Assert.That((long)result["logicalRecords"]["accepted"], Is.EqualTo(128));
            Assert.That((long)result["producerTotals"]["schedule"]["accepted"], Is.EqualTo(1));
            Assert.That((long)result["producerTotals"]["workEnqueued"], Is.EqualTo(2));
            Assert.That((long)result["consumerTotals"]["workDequeued"], Is.EqualTo(2));
            Assert.That((long)result["consumerTotals"]["workCompleted"], Is.EqualTo(2));
            Assert.That((long)result["producerTotals"]["chargedBytes"], Is.EqualTo((long)result["consumerTotals"]["releasedBytes"]));
            Assert.That((long)result["producerTotals"]["pendingPeakBytes"], Is.EqualTo(66048));
            Assert.That((long)result["producerTotals"]["pendingEndBytes"], Is.Zero);
            Assert.That((bool)result["observationComplete"], Is.True);
        }

        [Test] public void EveryEntryAndGuardKeepsOnlyItsFirstRejectionWithOriginalWatermarks()
        {
            var observer = Create();
            var first = new EvidenceQueueRejection { runId = "run", captureId = "capture", round = 7, sequence = 123,
                stage = "replay.input", phase = 0, requestedBytes = 512, pendingBytes = 1000, effectiveQueueLimit = 1100,
                budgetUsed = 2000, budgetLimit = 3000, produced = 123, written = 120, flushed = 118 };
            foreach (EvidenceQueueEntry entry in Enum.GetValues(typeof(EvidenceQueueEntry)))
            foreach (EvidenceQueueGuard guard in Enum.GetValues(typeof(EvidenceQueueGuard)))
            {
                Assert.That(observer.ShouldCaptureFirst(entry, guard), Is.True);
                observer.Rejected(entry, guard, first, Origin);
                Assert.That(observer.ShouldCaptureFirst(entry, guard), Is.False);
                observer.Rejected(entry, guard, default, Origin + 1);
            }
            var result = Export(observer);
            Assert.That(result["firstRejections"].Count(), Is.EqualTo(12));
            Assert.That((string)result["firstRejection"]["entry"], Is.EqualTo("TryWrite"));
            Assert.That((string)result["firstRejection"]["guard"], Is.EqualTo("Stopping"));
            foreach (var slot in result["firstRejections"])
            {
                Assert.That((long)slot["ticks"], Is.EqualTo(Origin));
                Assert.That((ulong)slot["context"]["sequence"], Is.EqualTo(123));
                Assert.That((ulong)slot["context"]["flushed"], Is.EqualTo(118));
                Assert.That((long)slot["context"]["effectiveQueueLimit"], Is.EqualTo(1100));
                Assert.That((long)slot["context"]["budgetUsed"], Is.EqualTo(2000));
            }
            Assert.That((long)result["logicalRecords"]["rejected"], Is.EqualTo(16));
            Assert.That((long)result["producerTotals"]["schedule"]["rejected"], Is.EqualTo(8));
        }

        [Test] public void CaptureFailureAndUnknownScheduleSourceNeverInventAnAdmissionRejectionSource()
        {
            var observer = Create();
            observer.Attempt(EvidenceQueueEntry.TryWrite, Origin); observer.CaptureFailed(EvidenceQueueEntry.TryWrite, Origin);
            observer.Attempt(EvidenceQueueEntry.Schedule, Origin);
            observer.Rejected(EvidenceQueueEntry.Schedule, EvidenceQueueGuard.BudgetReservation, default, Origin);
            var result = Export(observer);
            Assert.That((long)result["logicalRecords"]["captureFailed"], Is.EqualTo(1));
            Assert.That((long)result["logicalRecords"]["rejected"], Is.Zero);
            var context = result["firstRejection"]["context"];
            foreach (string field in new[] { "runId", "captureId", "round", "sequence", "phase", "produced", "written", "flushed" })
                Assert.That(context[field].Type, Is.EqualTo(JTokenType.Null), field);
        }

        [Test] public void OversizedIdentityReferencesAreNotRetainedOutsideFixedReservation()
        {
            var observer = Create();
            var context = new EvidenceQueueRejection { runId = new string('r', 101), captureId = new string('c', 101), stage = new string('s', 129) };
            observer.Rejected(EvidenceQueueEntry.TryWrite, EvidenceQueueGuard.QueueLimit, context, Origin);
            var result = Export(observer);
            Assert.That((bool)result["firstRejection"]["metadataTruncated"], Is.True);
            Assert.That(result["firstRejection"]["context"]["runId"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That(result["firstRejection"]["context"]["captureId"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That(result["firstRejection"]["context"]["stage"].Type, Is.EqualTo(JTokenType.Null));
        }

        [Test] public void SpansUseCompletionWindowAndServiceMaximumSurvivesLongQueueResidence()
        {
            var observer = Create(); long width = Stopwatch.Frequency / 10;
            long start = Origin + width - 5, end = Origin + width + 5;
            observer.Span(EvidenceQueueStage.Service, start, end);
            observer.Span(EvidenceQueueStage.Write, start + 1, end - 1);
            observer.Span(EvidenceQueueStage.QueueResidence, Origin, end);
            observer.Span(EvidenceQueueStage.FilesGateWait, start, start + 3);
            observer.Span(EvidenceQueueStage.StreamFlush, start, end - 2);
            observer.Span(EvidenceQueueStage.DurableFlush, start, end - 1);
            observer.ConsumerPending(1024, end);
            var result = Export(observer);
            Assert.That(result["consumerWindows"].Count(), Is.EqualTo(2));
            Assert.That((int)result["consumerWindows"][1]["index"], Is.EqualTo(1));
            var window = result["consumerWindows"][1]["values"];
            Assert.That((long)window["serviceTicks"], Is.EqualTo(10));
            Assert.That((long)window["writeTicks"], Is.EqualTo(8));
            Assert.That((long)window["maxServiceStartTicks"], Is.EqualTo(start));
            Assert.That((long)window["maxServiceEndTicks"], Is.EqualTo(end));
            Assert.That((string)window["maxSpanStage"], Is.EqualTo("QueueResidence"));
            Assert.That((long)window["pendingEndBytes"], Is.EqualTo(1024));
            Assert.That((long)window["maxStreamFlushStartTicks"], Is.EqualTo(start));
            Assert.That((long)window["maxStreamFlushEndTicks"], Is.EqualTo(end - 2));
            Assert.That((long)window["maxDurableFlushStartTicks"], Is.EqualTo(start));
            Assert.That((long)window["maxDurableFlushEndTicks"], Is.EqualTo(end - 1));
            Assert.That((long)result["consumerTotals"]["maxFilesGateWaitEndTicks"], Is.EqualTo(start + 3));
        }

        [Test] public void EveryConsumerStageIsReportedAndNestedDurationsAreNotAddedIntoService()
        {
            var observer = Create();
            foreach (EvidenceQueueStage stage in Enum.GetValues(typeof(EvidenceQueueStage))) observer.Span(stage, Origin, Origin + 7);
            var totals = Export(observer)["consumerTotals"];
            foreach (string field in new[] { "serviceTicks", "filesGateWaitTicks", "writeTicks", "streamFlushTicks", "durableFlushTicks",
                "maintenanceTicks", "idleTicks", "queueResidenceTicks", "maintenanceFlushTicks", "agedBlockFlushTicks", "pruneTicks",
                "idleCollectingTicks", "idleEmptyTicks" }) Assert.That((long)totals[field], Is.EqualTo(7), field);
        }

        [Test] public void WindowOverflowCannotOverwriteEarlierEvidenceAndTotalsRemainExact()
        {
            var observer = Create(); long width = Stopwatch.Frequency / 10;
            for (int i = 0; i < EvidenceQueueObservation.WindowCount + 3; i++)
            {
                observer.Attempt(EvidenceQueueEntry.TryWrite, Origin + width * i);
                observer.Dequeued(Origin + width * i);
            }
            var result = Export(observer);
            Assert.That(result["producerWindows"].Count(), Is.EqualTo(1024));
            Assert.That(result["consumerWindows"].Count(), Is.EqualTo(1024));
            Assert.That((int)result["producerWindows"][0]["index"], Is.Zero);
            Assert.That((long)result["producerOverflowEvents"], Is.EqualTo(3));
            Assert.That((long)result["consumerOverflowEvents"], Is.EqualTo(3));
            Assert.That((long)result["logicalRecords"]["attempts"], Is.EqualTo(1027));
            Assert.That((long)result["consumerTotals"]["workDequeued"], Is.EqualTo(1027));
            Assert.That((bool)result["overflow"], Is.True);
            Assert.That((bool)result["observationComplete"], Is.False);
        }

        [Test] public void SeparateProducerAndConsumerWritersPreserveCountsWithoutObservationLocks()
        {
            var observer = Create();
            var consumer = new Thread(() => { for (int i = 0; i < 10000; i++) { observer.Dequeued(Origin); observer.Completed(512, Origin); } });
            consumer.Start();
            for (int i = 0; i < 10000; i++)
            { observer.Attempt(EvidenceQueueEntry.TryWrite, Origin); observer.Accepted(EvidenceQueueEntry.TryWrite, Origin); observer.Enqueued(512, 512, Origin); }
            Assert.That(consumer.Join(5000), Is.True);
            var result = Export(observer);
            Assert.That((long)result["logicalRecords"]["accepted"], Is.EqualTo(10000));
            Assert.That((long)result["consumerTotals"]["workCompleted"], Is.EqualTo(10000));
        }

        [Test] public void TimeoutDoesNotExportOrReleaseButLaterSuccessfulJoinCanExport()
        {
            var memory = new DiagnosticMemoryBudget(EvidenceQueueObservation.ReservedBytes);
            EvidenceQueueObservation.TryCreate(memory, true, out var observer, Origin);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            try
            {
                Assert.That(observer.StopAndExport(path, false, true), Is.False);
                Assert.That(observer.StopAndExport(path, true, false), Is.False);
                Assert.That(File.Exists(path), Is.False); Assert.That(memory.Used, Is.EqualTo(EvidenceQueueObservation.ReservedBytes));
                Assert.That(observer.StopAndExport(path, true, true), Is.True);
                Assert.That(observer.StopAndExport(path, true, true), Is.False);
                Assert.That(memory.Used, Is.Zero);
            }
            finally { observer.ReleaseAfterStop(true, true); if (File.Exists(path)) File.Delete(path); }
        }

        [Test] public void ExportFailureReleasesExactlyOnceWithoutOverwritingExistingEvidence()
        {
            var memory = new DiagnosticMemoryBudget(EvidenceQueueObservation.ReservedBytes);
            EvidenceQueueObservation.TryCreate(memory, true, out var observer, Origin);
            string path = Path.GetTempFileName(); File.WriteAllText(path, "original");
            try
            {
                Assert.Throws<IOException>(() => observer.StopAndExport(path, true, true));
                Assert.That(File.ReadAllText(path), Is.EqualTo("original")); Assert.That(memory.Used, Is.Zero);
                Assert.That(observer.ReleaseAfterStop(true, true), Is.False);
            }
            finally { observer.ReleaseAfterStop(true, true); File.Delete(path); }
        }

        [Test] public void PhasesUseCommonOriginAndCannotOverwriteTheFirstBoundary()
        {
            var observer = Create();
            observer.MarkPhase(EvidenceQueuePhase.Load, Origin + 10); observer.MarkPhase(EvidenceQueuePhase.Load, Origin + 99);
            observer.MarkPhase(EvidenceQueuePhase.Catchup, Origin + 20); observer.MarkPhase(EvidenceQueuePhase.Close, Origin + 30);
            var result = Export(observer);
            Assert.That((long)result["originTicks"], Is.EqualTo(Origin));
            Assert.That(result["phases"].Count(), Is.EqualTo(4));
            Assert.That((string)result["phases"][0]["phase"], Is.EqualTo("Init"));
            Assert.That((long)result["phases"][1]["ticks"], Is.EqualTo(Origin + 10));
        }

        [Test] public void InconsistentCountsOrUnfinishedWorkCannotBeACompleteObservation()
        {
            var observer = Create();
            observer.Attempt(EvidenceQueueEntry.TryWrite, Origin);
            observer.Enqueued(512, 512, Origin); observer.Dequeued(Origin);
            var result = Export(observer);
            Assert.That((bool)result["countsBalanced"], Is.False);
            Assert.That((bool)result["observationComplete"], Is.False);
            Assert.That((long)result["unfinishedWorkItems"], Is.EqualTo(1));
        }

        private const long Origin = 1000;
        private static EvidenceQueueObservation Create()
        {
            Assert.That(EvidenceQueueObservation.TryCreate(new DiagnosticMemoryBudget(), true, out var observer, Origin), Is.True);
            return observer;
        }
        private static JObject Export(EvidenceQueueObservation observer)
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            try { Assert.That(observer.StopAndExport(path, true, true), Is.True); return JObject.Parse(File.ReadAllText(path)); }
            finally { observer.ReleaseAfterStop(true, true); if (File.Exists(path)) File.Delete(path); }
        }
    }
}
