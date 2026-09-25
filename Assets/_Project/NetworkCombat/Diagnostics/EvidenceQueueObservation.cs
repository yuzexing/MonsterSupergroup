using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Newtonsoft.Json;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    public enum EvidenceQueueEntry { TryWrite, TryWriteAdvance, Schedule }
    public enum EvidenceQueueGuard { Stopping, OversizedRecord, QueueLimit, BudgetReservation }
    public enum EvidenceQueuePhase { Init, Load, Catchup, Close }
    public enum EvidenceQueueStage
    {
        Service, FilesGateWait, Write, StreamFlush, DurableFlush, Maintenance, Idle,
        QueueResidence, MaintenanceFlush, AgedBlockFlush, Prune, IdleCollecting, IdleEmpty
    }

    public struct EvidenceQueueRejection
    {
        public string runId, captureId, stage;
        public uint? round;
        public ulong? sequence, produced, written, flushed;
        public int? phase;
        public long requestedBytes, pendingBytes, effectiveQueueLimit, budgetUsed, budgetLimit;
    }

    /// <summary>
    /// Opt-in numeric observations: producer calls use the store gate; consumer calls have one writer.
    /// Detailed buffers are read only after both writers stop. Partial exports use a separate atomic progress mirror.
    /// Counts for Schedule are calls/work, never logical evidence records.
    /// </summary>
    public sealed partial class EvidenceQueueObservation
    {
        public const int WindowCount = 1024, WindowMilliseconds = 100, ReservedBytes = 512 << 10;
        // Three long counters/origin, opt-in flag, configured resolution and conservative alignment.
        public const int DeferredWindowMetadataBytes = 40;
        public static long Now => Stopwatch.GetTimestamp();
        private readonly DiagnosticMemoryBudget memory;
        private readonly long originTicks, windowTicks;
        private readonly int configuredWindowMilliseconds;
        private readonly bool deferredWindows;
        private long windowOriginTicks, producerBeforeWindowEvents, consumerBeforeWindowEvents;
        private ProducerWindow[] producerWindows;
        private ConsumerWindow[] consumerWindows;
        private bool[] producerUsed, consumerUsed;
        private RejectionSlot[] rejections;
        private long[] phases;
        private bool[] phaseSet;
        private RejectionSlot firstRejection;
        private ProducerWindow producerTotals;
        private ConsumerWindow consumerTotals;
        private long producerOverflowEvents, consumerOverflowEvents;
        private int released;

        private struct EntryCounts { public long attempts, accepted, rejected, captureFailed; }
        private struct ProducerWindow
        {
            public EntryCounts tryWrite, tryWriteAdvance, schedule;
            public long workEnqueued, chargedBytes, pendingEndBytes, pendingPeakBytes;
        }
        private struct ConsumerWindow
        {
            public long workDequeued, workCompleted, releasedBytes, pendingEndBytes, pendingPeakBytes;
            public long serviceTicks, filesGateWaitTicks, writeTicks, streamFlushTicks, durableFlushTicks;
            public long maintenanceTicks, idleTicks, queueResidenceTicks, maintenanceFlushTicks;
            public long agedBlockFlushTicks, pruneTicks, idleCollectingTicks, idleEmptyTicks;
            public long maxSpanStartTicks, maxSpanEndTicks, maxServiceStartTicks, maxServiceEndTicks;
            public long maxFilesGateWaitStartTicks, maxFilesGateWaitEndTicks, maxStreamFlushStartTicks, maxStreamFlushEndTicks;
            public long maxDurableFlushStartTicks, maxDurableFlushEndTicks;
            public EvidenceQueueStage maxSpanStage;
        }
        private struct RejectionSlot
        {
            public bool present, metadataTruncated;
            public EvidenceQueueEntry entry;
            public EvidenceQueueGuard guard;
            public long ticks;
            public EvidenceQueueRejection context;
        }

        private EvidenceQueueObservation(DiagnosticMemoryBudget memory, long origin, bool deferWindows, int windowMilliseconds)
        {
            this.memory = memory; originTicks = origin;
            configuredWindowMilliseconds = windowMilliseconds;
            windowTicks = Math.Max(1, Stopwatch.Frequency * windowMilliseconds / 1000);
            deferredWindows = deferWindows; windowOriginTicks = deferWindows ? 0 : origin;
            producerWindows = new ProducerWindow[WindowCount]; consumerWindows = new ConsumerWindow[WindowCount];
            producerUsed = new bool[WindowCount]; consumerUsed = new bool[WindowCount];
            rejections = new RejectionSlot[12]; phases = new long[4]; phaseSet = new bool[4];
            phases[0] = origin; phaseSet[0] = true;
            InitializeServiceObservation();
            InitializeMainObservation();
        }

        public static bool TryCreate(DiagnosticMemoryBudget memory, bool enabled, out EvidenceQueueObservation observer, long originTicks = 0, bool deferWindows = false, int windowMilliseconds = WindowMilliseconds)
        {
            observer = null;
            if (windowMilliseconds != WindowMilliseconds && windowMilliseconds != 1000)
                throw new ArgumentOutOfRangeException(nameof(windowMilliseconds));
            if (!enabled || memory == null || !memory.TryReserve(ReservedBytes)) return false;
            try { observer = new EvidenceQueueObservation(memory, originTicks == 0 ? Now : originTicks, deferWindows, windowMilliseconds); return true; }
            catch { memory.Release(ReservedBytes); throw; }
        }

        private bool Active => Volatile.Read(ref released) == 0;
        private int Window(long ticks, bool producer)
        {
            long windowOrigin = Volatile.Read(ref windowOriginTicks);
            if (deferredWindows && (windowOrigin == 0 || ticks < windowOrigin))
            {
                if (producer) producerBeforeWindowEvents++; else consumerBeforeWindowEvents++;
                return -1;
            }
            long index = Math.Max(0, ticks - windowOrigin) / windowTicks;
            if (index >= WindowCount)
            {
                if (producer) producerOverflowEvents++; else consumerOverflowEvents++;
                return -1;
            }
            if (producer) producerUsed[index] = true; else consumerUsed[index] = true;
            return (int)index;
        }
        private static ref EntryCounts Counts(ref ProducerWindow window, EvidenceQueueEntry entry)
        {
            if (entry == EvidenceQueueEntry.TryWrite) return ref window.tryWrite;
            if (entry == EvidenceQueueEntry.TryWriteAdvance) return ref window.tryWriteAdvance;
            return ref window.schedule;
        }
        private static void Count(ref ProducerWindow window, EvidenceQueueEntry entry, int kind)
        {
            ref var count = ref Counts(ref window, entry);
            if (kind == 0) count.attempts++;
            else if (kind == 1) count.accepted++;
            else if (kind == 2) count.rejected++;
            else count.captureFailed++;
        }
        private void ProducerCount(EvidenceQueueEntry entry, long ticks, int kind)
        {
            if (!Active) return;
            Interlocked.Increment(ref partialEntryCounts[(int)entry * 4 + kind]);
            Count(ref producerTotals, entry, kind);
            int index = Window(ticks, true);
            if (index >= 0) Count(ref producerWindows[index], entry, kind);
        }
        public void Attempt(EvidenceQueueEntry entry, long ticks) => ProducerCount(entry, ticks, 0);
        public void Accepted(EvidenceQueueEntry entry, long ticks) => ProducerCount(entry, ticks, 1);
        public void CaptureFailed(EvidenceQueueEntry entry, long ticks) => ProducerCount(entry, ticks, 3);

        public bool ShouldCaptureFirst(EvidenceQueueEntry entry, EvidenceQueueGuard guard) =>
            Active && !rejections[(int)entry * 4 + (int)guard].present;

        public void Rejected(EvidenceQueueEntry entry, EvidenceQueueGuard guard, in EvidenceQueueRejection rejection, long ticks)
        {
            if (!Active) return;
            Interlocked.Increment(ref partialGuardCounts[(int)guard]);
            ProducerCount(entry, ticks, 2);
            int slot = (int)entry * 4 + (int)guard;
            if (rejections[slot].present) return;
            var bounded = rejection;
            bool truncated = false;
            bounded.runId = Bounded(bounded.runId, 100, ref truncated);
            bounded.captureId = Bounded(bounded.captureId, 100, ref truncated);
            bounded.stage = Bounded(bounded.stage, 128, ref truncated);
            var value = new RejectionSlot { present = true, entry = entry, guard = guard, ticks = ticks,
                context = bounded, metadataTruncated = truncated };
            rejections[slot] = value;
            if (!firstRejection.present)
            { firstRejection = value; Volatile.Write(ref partialFirstRejection, new PartialRejection(value)); CaptureServiceRejection(ticks); }
        }
        private static string Bounded(string value, int limit, ref bool truncated)
        {
            if (value == null || value.Length <= limit) return value;
            truncated = true; return null;
        }
        private static void ProducerPending(ref ProducerWindow window, long pending)
        { window.pendingEndBytes = pending; window.pendingPeakBytes = Math.Max(window.pendingPeakBytes, pending); }
        public void Enqueued(long bytes, long pendingAfter, long ticks)
        {
            if (!Active) return;
            Interlocked.Increment(ref partialWorkEnqueued); Interlocked.Add(ref partialChargedBytes, bytes);
            Interlocked.Exchange(ref partialPendingBytes, pendingAfter);
            producerTotals.workEnqueued++; producerTotals.chargedBytes += bytes; ProducerPending(ref producerTotals, pendingAfter);
            int index = Window(ticks, true);
            if (index < 0) return;
            producerWindows[index].workEnqueued++; producerWindows[index].chargedBytes += bytes;
            ProducerPending(ref producerWindows[index], pendingAfter);
        }
        // Called under the producer gate even when the worker observes/reduces pending bytes.
        public void Pending(long pending, long ticks)
        {
            if (!Active) return;
            Interlocked.Exchange(ref partialPendingBytes, pending);
            ProducerPending(ref producerTotals, pending);
            int index = Window(ticks, true);
            if (index >= 0) ProducerPending(ref producerWindows[index], pending);
        }
        // Consumer-only pending samples must be captured while the caller holds the store gate.
        public void ConsumerPending(long pending, long ticks)
        {
            if (!Active) return;
            consumerTotals.pendingEndBytes = pending; consumerTotals.pendingPeakBytes = Math.Max(consumerTotals.pendingPeakBytes, pending);
            int index = Window(ticks, false);
            if (index < 0) return;
            consumerWindows[index].pendingEndBytes = pending;
            consumerWindows[index].pendingPeakBytes = Math.Max(consumerWindows[index].pendingPeakBytes, pending);
        }
        public void Dequeued(long ticks)
        {
            if (!Active) return;
            Interlocked.Increment(ref partialWorkDequeued);
            consumerTotals.workDequeued++;
            int index = Window(ticks, false);
            if (index >= 0) consumerWindows[index].workDequeued++;
        }
        public void Completed(long bytes, long ticks)
        {
            if (!Active) return;
            Interlocked.Increment(ref partialWorkCompleted); Interlocked.Add(ref partialReleasedBytes, bytes);
            consumerTotals.workCompleted++; consumerTotals.releasedBytes += bytes;
            int index = Window(ticks, false);
            if (index >= 0) { consumerWindows[index].workCompleted++; consumerWindows[index].releasedBytes += bytes; }
        }
        private static void AddSpan(ref ConsumerWindow window, EvidenceQueueStage stage, long start, long end)
        {
            long duration = Math.Max(0, end - start);
            switch (stage)
            {
                case EvidenceQueueStage.Service: window.serviceTicks += duration; break;
                case EvidenceQueueStage.FilesGateWait: window.filesGateWaitTicks += duration; break;
                case EvidenceQueueStage.Write: window.writeTicks += duration; break;
                case EvidenceQueueStage.StreamFlush: window.streamFlushTicks += duration; break;
                case EvidenceQueueStage.DurableFlush: window.durableFlushTicks += duration; break;
                case EvidenceQueueStage.Maintenance: window.maintenanceTicks += duration; break;
                case EvidenceQueueStage.Idle: window.idleTicks += duration; break;
                case EvidenceQueueStage.QueueResidence: window.queueResidenceTicks += duration; break;
                case EvidenceQueueStage.MaintenanceFlush: window.maintenanceFlushTicks += duration; break;
                case EvidenceQueueStage.AgedBlockFlush: window.agedBlockFlushTicks += duration; break;
                case EvidenceQueueStage.Prune: window.pruneTicks += duration; break;
                case EvidenceQueueStage.IdleCollecting: window.idleCollectingTicks += duration; break;
                case EvidenceQueueStage.IdleEmpty: window.idleEmptyTicks += duration; break;
            }
            if (duration > window.maxSpanEndTicks - window.maxSpanStartTicks)
            { window.maxSpanStage = stage; window.maxSpanStartTicks = start; window.maxSpanEndTicks = end; }
            if (stage == EvidenceQueueStage.Service && duration > window.maxServiceEndTicks - window.maxServiceStartTicks)
            { window.maxServiceStartTicks = start; window.maxServiceEndTicks = end; }
            if (stage == EvidenceQueueStage.FilesGateWait && duration > window.maxFilesGateWaitEndTicks - window.maxFilesGateWaitStartTicks)
            { window.maxFilesGateWaitStartTicks = start; window.maxFilesGateWaitEndTicks = end; }
            if (stage == EvidenceQueueStage.StreamFlush && duration > window.maxStreamFlushEndTicks - window.maxStreamFlushStartTicks)
            { window.maxStreamFlushStartTicks = start; window.maxStreamFlushEndTicks = end; }
            if (stage == EvidenceQueueStage.DurableFlush && duration > window.maxDurableFlushEndTicks - window.maxDurableFlushStartTicks)
            { window.maxDurableFlushStartTicks = start; window.maxDurableFlushEndTicks = end; }
        }
        public void Span(EvidenceQueueStage stage, long start, long end)
        {
            if (!Active) return;
            Interlocked.Add(ref partialQueueStageTicks[(int)stage], Math.Max(0, end - start));
            AddSpan(ref consumerTotals, stage, start, end);
            int index = Window(end, false);
            if (index >= 0) AddSpan(ref consumerWindows[index], stage, start, end);
        }
        public void MarkPhase(EvidenceQueuePhase phase, long ticks)
        {
            if (!Active || phaseSet[(int)phase]) return;
            phases[(int)phase] = ticks; phaseSet[(int)phase] = true;
            if (deferredWindows && phase == EvidenceQueuePhase.Load)
                Interlocked.CompareExchange(ref windowOriginTicks, ticks, 0);
        }

        public bool StopAndExport(string path, bool producerStopped, bool consumerJoined)
        {
            if (!producerStopped || !consumerJoined || !Active || !TryStopMainObservation()) return false;
            PublishObservationFile(path, (writer, serializer) =>
            {
                writer.WriteStartObject();
                Property(writer, serializer, "schemaVersion", 1);
                Property(writer, serializer, "originTicks", originTicks);
                Property(writer, serializer, "windowSchemaVersion", 2);
                Property(writer, serializer, "windowTimeOrigin", "windowOriginTicks");
                Property(writer, serializer, "windowOriginTicks", windowOriginTicks == 0 ? (long?)null : windowOriginTicks);
                Property(writer, serializer, "deferredWindows", deferredWindows);
                Property(writer, serializer, "deferredWindowMetadataBytes", DeferredWindowMetadataBytes);
                Property(writer, serializer, "producerBeforeWindowEvents", producerBeforeWindowEvents);
                Property(writer, serializer, "consumerBeforeWindowEvents", consumerBeforeWindowEvents);
                Property(writer, serializer, "beforeWindowAttribution", "event calls, not logical records; totals and service identities retain pre-window work");
                Property(writer, serializer, "stopwatchFrequency", Stopwatch.Frequency);
                Property(writer, serializer, "windowMilliseconds", configuredWindowMilliseconds);
                Property(writer, serializer, "windowCapacity", WindowCount);
                Property(writer, serializer, "reservedBytes", ReservedBytes);
                Property(writer, serializer, "windowStorageBytes", ObservationWindowStorageBytes);
                Property(writer, serializer, "existingAllowanceBytes", 64 << 10);
                Property(writer, serializer, "serviceStorageBytes", ServiceStorageBytes);
                Property(writer, serializer, "mainStorageBytes", MainStorageBytes);
                Property(writer, serializer, "accountedStorageBytes", AccountedStorageBytes);
                Property(writer, serializer, "spanAttribution", "completion-window; nested stages are not additive wall time");
                Property(writer, serializer, "pendingAttribution", "observed samples only; absent sparse windows do not imply zero pending bytes");
                Property(writer, serializer, "producerOverflowEvents", producerOverflowEvents);
                Property(writer, serializer, "consumerOverflowEvents", consumerOverflowEvents);
                bool overflow = producerOverflowEvents != 0 || consumerOverflowEvents != 0;
                long unfinished = producerTotals.workEnqueued - consumerTotals.workCompleted;
                bool balanced = CountsBalanced;
                Property(writer, serializer, "overflow", overflow);
                Property(writer, serializer, "countsBalanced", balanced);
                Property(writer, serializer, "unfinishedWorkItems", unfinished);
                bool windowStarted = windowOriginTicks != 0;
                Property(writer, serializer, "windowCoverageStarted", windowStarted);
                Property(writer, serializer, "windowCoverageComplete", windowStarted && !overflow && balanced);
                Property(writer, serializer, "observationComplete", windowStarted && !overflow && balanced &&
                    producerBeforeWindowEvents == 0 && consumerBeforeWindowEvents == 0);
                Property(writer, serializer, "producerTotals", producerTotals);
                Property(writer, serializer, "consumerTotals", consumerTotals);
                Property(writer, serializer, "logicalRecords", new EntryCounts {
                    attempts = producerTotals.tryWrite.attempts + producerTotals.tryWriteAdvance.attempts,
                    accepted = producerTotals.tryWrite.accepted + producerTotals.tryWriteAdvance.accepted,
                    rejected = producerTotals.tryWrite.rejected + producerTotals.tryWriteAdvance.rejected,
                    captureFailed = producerTotals.tryWrite.captureFailed + producerTotals.tryWriteAdvance.captureFailed });
                writer.WritePropertyName("phases"); writer.WriteStartArray();
                for (int i = 0; i < phaseSet.Length; i++) if (phaseSet[i])
                {
                    writer.WriteStartObject(); Property(writer, serializer, "phase", ((EvidenceQueuePhase)i).ToString());
                    Property(writer, serializer, "ticks", phases[i]); writer.WriteEndObject();
                }
                writer.WriteEndArray();
                Property(writer, serializer, "firstRejection", firstRejection.present ? (object)firstRejection : null);
                writer.WritePropertyName("firstRejections"); writer.WriteStartArray();
                foreach (var rejection in rejections) if (rejection.present) serializer.Serialize(writer, rejection);
                writer.WriteEndArray();
                Windows(writer, serializer, "producerWindows", producerWindows, producerUsed);
                Windows(writer, serializer, "consumerWindows", consumerWindows, consumerUsed);
                WriteServiceObservation(writer, serializer);
                WriteMainObservation(writer, serializer);
                writer.WriteEndObject();
            });
            // Keep stopped observations available for a retry if opening, serialization or closing fails.
            ReleaseAfterStop(true, true);
            return true;
        }
        private static void PublishObservationFile(string path, Action<JsonTextWriter, JsonSerializer> write)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096))
                using (var stream = new StreamWriter(file))
                using (var writer = new JsonTextWriter(stream))
                {
                    var serializer = new JsonSerializer();
                    serializer.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
                    write(writer, serializer);
                    writer.Flush(); stream.Flush(); file.Flush(true);
                }
                // Publish only after the complete file has been durably flushed and closed.
                File.Move(temporary, path);
            }
            finally
            {
                // Only this invocation's unpublished temporary file is disposable; never overwrite an existing result.
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }
        private static void Property(JsonWriter writer, JsonSerializer serializer, string name, object value)
        { writer.WritePropertyName(name); serializer.Serialize(writer, value); }
        private static bool Balanced(EntryCounts count) => count.attempts == count.accepted + count.rejected + count.captureFailed;
        private bool CountsBalanced => Balanced(producerTotals.tryWrite) && Balanced(producerTotals.tryWriteAdvance) && Balanced(producerTotals.schedule) &&
            producerTotals.workEnqueued == consumerTotals.workDequeued && producerTotals.workEnqueued == consumerTotals.workCompleted &&
            producerTotals.chargedBytes == consumerTotals.releasedBytes && producerTotals.pendingEndBytes == 0;
        // Scalar totals remain available after export releases the detailed arrays. Never use them
        // as a live atomic snapshot while either producer or consumer can still update them.
        public bool CountsBalancedAfterStop(bool producerStopped, bool consumerJoined) => producerStopped && consumerJoined && CountsBalanced;
        private static void Windows<T>(JsonWriter writer, JsonSerializer serializer, string name, T[] values, bool[] used)
        {
            writer.WritePropertyName(name); writer.WriteStartArray();
            for (int i = 0; i < values.Length; i++) if (used[i])
            {
                writer.WriteStartObject(); Property(writer, serializer, "index", i);
                Property(writer, serializer, "values", values[i]); writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        public bool ReleaseAfterStop(bool producerStopped, bool consumerJoined)
        {
            if (!producerStopped || !consumerJoined || !TryStopMainObservation() || Interlocked.Exchange(ref released, 1) != 0) return false;
            producerWindows = null; consumerWindows = null; producerUsed = null; consumerUsed = null;
            rejections = null; phases = null; phaseSet = null; firstRejection = default;
            ReleaseServiceObservation();
            ReleaseMainObservation();
            ReleasePartialObservation();
            memory.Release(ReservedBytes);
            return true;
        }
    }
}
