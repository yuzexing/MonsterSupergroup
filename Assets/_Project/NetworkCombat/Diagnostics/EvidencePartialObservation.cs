using System.Diagnostics;
using System.Threading;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    public sealed partial class EvidenceQueueObservation
    {
        // Fixed mirrors, array headers, the small work/rejection objects and bounded source strings.
        // Engine/operation/stage and rejection strings share the already charged bounded identities.
        public const int PartialStorageBytes = 2560;
        private long[] partialEntryCounts = new long[12], partialGuardCounts = new long[4];
        private long[] partialQueueStageTicks = new long[(int)EvidenceQueueStage.IdleEmpty + 1];
        private long[] partialServiceInclusive = new long[ServiceStageCount], partialServiceExclusive = new long[ServiceStageCount];
        private long partialWorkEnqueued, partialChargedBytes, partialPendingBytes, partialWorkDequeued, partialWorkCompleted, partialReleasedBytes;
        private int partialCurrentStage = -1;
        private readonly object partialProgressGate = new();
        private PartialWork partialWork;
        private PartialRejection partialFirstRejection;

        private struct PartialWork
        {
            public bool active, metadataTruncated;
            public int context;
            public long id, queuedAt, bytes, startedTicks, endedTicks;
            public EvidenceServiceIdentity identity;
        }
        private sealed class PartialRejection
        {
            public readonly bool present, metadataTruncated;
            public readonly EvidenceQueueEntry entry;
            public readonly EvidenceQueueGuard guard;
            public readonly long ticks;
            public readonly EvidenceQueueRejection context;
            public PartialRejection(RejectionSlot value)
            { present = value.present; metadataTruncated = value.metadataTruncated; entry = value.entry; guard = value.guard; ticks = value.ticks; context = value.context; }
        }

        private void BeginPartialContext(int context, long started, long id, long queuedAt, long bytes)
        {
            // These locks cover only fixed metadata assignments, never the service, IO or stage arrays.
            lock (partialProgressGate)
                partialWork = new PartialWork { active = true, context = context, id = id, queuedAt = queuedAt, bytes = bytes,
                    startedTicks = started, identity = new EvidenceServiceIdentity { kind = EvidenceServiceWorkKind.Schedule, phase = -1 } };
            Volatile.Write(ref partialCurrentStage, -1);
        }
        private void BindPartialIdentity(in EvidenceServiceIdentity identity)
        {
            var bounded = identity; bool truncated = false;
            bounded.runId = Bounded(identity.runId, ServiceSourceLength, ref truncated);
            bounded.captureId = Bounded(identity.captureId, ServiceSourceLength, ref truncated);
            bounded.engine = Bounded(identity.engine, ServiceIdentityLength, ref truncated);
            bounded.operation = Bounded(identity.operation, ServiceIdentityLength, ref truncated);
            bounded.stage = Bounded(identity.stage, ServiceRecordStageLength, ref truncated);
            lock (partialProgressGate) { partialWork.identity = bounded; partialWork.metadataTruncated = truncated; }
        }
        private void EndPartialContext(long ended)
        {
            lock (partialProgressGate) { partialWork.active = false; partialWork.endedTicks = ended; }
            Volatile.Write(ref partialCurrentStage, -1);
        }
        private static long[] ReadPartialCounters(long[] source)
        {
            var result = new long[source.Length];
            for (int i = 0; i < result.Length; i++) result[i] = Interlocked.Read(ref source[i]);
            return result;
        }
        private void ReleasePartialObservation()
        {
            // Exporters retain local array references before reading. Detach rather than clear their contents.
            Volatile.Write(ref partialEntryCounts, null); Volatile.Write(ref partialGuardCounts, null);
            Volatile.Write(ref partialQueueStageTicks, null);
            Volatile.Write(ref partialServiceInclusive, null); Volatile.Write(ref partialServiceExclusive, null);
            Volatile.Write(ref partialFirstRejection, null);
            lock (partialProgressGate) partialWork = default;
            Volatile.Write(ref partialCurrentStage, -1);
        }

        /// <summary>Save a bounded, non-atomic progress sample without joining or releasing the observer.</summary>
        public bool ExportPartial(string path, bool producerStopped)
        {
            if (!producerStopped || !Active) return false;
            long started = Now;
            var entrySource = Volatile.Read(ref partialEntryCounts); var guardSource = Volatile.Read(ref partialGuardCounts);
            var queueSource = Volatile.Read(ref partialQueueStageTicks);
            var inclusiveSource = Volatile.Read(ref partialServiceInclusive); var exclusiveSource = Volatile.Read(ref partialServiceExclusive);
            if (!Active || entrySource == null || guardSource == null || queueSource == null || inclusiveSource == null || exclusiveSource == null) return false;
            var entries = ReadPartialCounters(entrySource); var guards = ReadPartialCounters(guardSource);
            var queueStages = ReadPartialCounters(queueSource);
            var inclusive = ReadPartialCounters(inclusiveSource); var exclusive = ReadPartialCounters(exclusiveSource);
            var rejection = Volatile.Read(ref partialFirstRejection);
            long enqueued = Interlocked.Read(ref partialWorkEnqueued), charged = Interlocked.Read(ref partialChargedBytes), pending = Interlocked.Read(ref partialPendingBytes);
            long dequeued = Interlocked.Read(ref partialWorkDequeued), completed = Interlocked.Read(ref partialWorkCompleted), releasedBytes = Interlocked.Read(ref partialReleasedBytes);
            int currentStage = Volatile.Read(ref partialCurrentStage);
            PartialWork work = default; bool workAvailable = Monitor.TryEnter(partialProgressGate, 0);
            if (workAvailable) { try { work = partialWork; } finally { Monitor.Exit(partialProgressGate); } }
            long ended = Now;
            if (!Active) return false;
            // Everything below serializes detached scalars/arrays or an immutable rejection publication.
            PublishObservationFile(path, (writer, serializer) =>
            {
                writer.WriteStartObject();
                Property(writer, serializer, "schemaVersion", 1); Property(writer, serializer, "kind", "PartialProgress");
                Property(writer, serializer, "producerStopped", true); Property(writer, serializer, "writerJoined", false);
                Property(writer, serializer, "observationComplete", false); Property(writer, serializer, "countsBalanced", null);
                Property(writer, serializer, "snapshotAtomic", false); Property(writer, serializer, "windowCoverageComplete", false);
                Property(writer, serializer, "detailWindowsIncluded", false); Property(writer, serializer, "mainDetailsIncluded", false);
                Property(writer, serializer, "evidenceCompleteness", "NotEvaluated"); Property(writer, serializer, "observerReleased", false);
                Property(writer, serializer, "originTicks", originTicks); Property(writer, serializer, "stopwatchFrequency", Stopwatch.Frequency);
                Property(writer, serializer, "snapshotReadStartedTicks", started); Property(writer, serializer, "snapshotReadEndedTicks", ended);
                Property(writer, serializer, "partialStorageBytes", PartialStorageBytes);
                Property(writer, serializer, "counterAttribution", "independent atomic counters; no cross-field conservation or completion assertion");
                Property(writer, serializer, "producerTotals", new { tryWrite = PartialEntry(entries, 0), tryWriteAdvance = PartialEntry(entries, 4), schedule = PartialEntry(entries, 8),
                    workEnqueued = enqueued, chargedBytes = charged, pendingEndBytes = pending });
                Property(writer, serializer, "consumerTotals", new { workDequeued = dequeued, workCompleted = completed, releasedBytes });
                Property(writer, serializer, "firstRejection", rejection);
                writer.WritePropertyName("guardCounts"); writer.WriteStartArray();
                for (int i = 0; i < guards.Length; i++) serializer.Serialize(writer, new { guard = (EvidenceQueueGuard)i, rejected = guards[i] });
                writer.WriteEndArray();
                Property(writer, serializer, "currentWorkAvailable", workAvailable);
                Property(writer, serializer, "currentWork", workAvailable ? (object)work : null);
                Property(writer, serializer, "currentStage", currentStage < 0 ? null : (object)(EvidenceServiceStage)currentStage);
                Property(writer, serializer, "currentStageAssociationAtomic", false);
                Property(writer, serializer, "serviceAttribution", "completed scopes only, including work and background contexts; open scopes excluded; nested inclusive ticks are not additive");
                writer.WritePropertyName("serviceStages"); writer.WriteStartArray();
                for (int i = 0; i < ServiceStageCount; i++) serializer.Serialize(writer, new { stage = (EvidenceServiceStage)i, inclusiveTicks = inclusive[i], exclusiveTicks = exclusive[i] });
                writer.WriteEndArray();
                writer.WritePropertyName("queueStages"); writer.WriteStartArray();
                for (int i = 0; i < queueStages.Length; i++) serializer.Serialize(writer, new { stage = (EvidenceQueueStage)i, ticks = queueStages[i] });
                writer.WriteEndArray(); writer.WriteEndObject();
            });
            return true;
        }
        private static object PartialEntry(long[] values, int start) => new {
            attempts = values[start], accepted = values[start + 1], rejected = values[start + 2], captureFailed = values[start + 3] };
    }
}
