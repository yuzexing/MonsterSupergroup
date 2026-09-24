using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Newtonsoft.Json;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    public enum EvidenceServiceWorkKind { Unknown, Record, Advance, Schedule }
    public enum EvidenceServiceContext { Startup, Maintenance, Close }
    public enum EvidenceServiceBlockKind { JsonLine, Advance }
    public enum EvidenceServiceStage
    {
        Append, SourceOpen, DirectoryCreate, SourceRecover, PreviousSourceClose, Manifest, Rotation,
        SharedInputs, InputJson, RecordJson, Payload, PayloadUtf8, PayloadHash, PayloadCompress,
        PayloadReserve, PayloadOpen, PayloadWrite, PayloadFlush, PayloadIndex, AtomicWrite,
        AtomicUtf8, AtomicOpen, AtomicWriteBytes, AtomicFlush, AtomicReplace, AtomicRetryWait,
        EventAppend, BlockEncode, BlockWorkspace, BlockBuild, BlockHash, BlockCompress, BlockHeader,
        StreamWrite, StreamFlush, DurableFlush, SourceCoverage, LeaseRelease, TerminalGateWait,
        MemoryRelease, FilesGateWait, Prune, Startup, Maintenance, SourceIndex, StorageReserve, JsonEncode, TerminalGate, EventOpen
    }

    public struct EvidenceServiceIdentity
    {
        public EvidenceServiceWorkKind kind;
        public string runId, captureId, engine, operation, stage;
        public uint round;
        public ulong firstSequence, lastSequence;
        public int logicalCount, phase;
        public bool mixedPhases;
    }

    // Ambient state is writer-thread-only. Replication ImportBlock I/O on another thread is not observed here.
    public static class EvidenceServiceTiming
    {
        [ThreadStatic] internal static EvidenceQueueObservation Current;
        public static EvidenceServiceScope Measure(EvidenceServiceStage stage)
        {
            var observer = Current;
            return observer == null || !observer.HasServiceContext ? default : new EvidenceServiceScope(observer, stage);
        }
        public static void RecordIoRetry(int hresult, int attempt = -1, bool willRetry = true) =>
            Current?.RecordIoRetry(hresult, attempt, willRetry);
        public static void RecordFlushedBlock(string runId, uint round, string captureId, ulong first, ulong last,
            int count, EvidenceServiceBlockKind kind) => Current?.RecordFlushedBlock(runId, round, captureId, first, last, count, kind);
        public static void RecordFlushedBlockFromDirectory(string directory, ulong first, ulong last, int count,
            EvidenceServiceBlockKind kind) => Current?.RecordFlushedBlockFromDirectory(directory, first, last, count, kind);
    }

    public readonly struct EvidenceServiceScope : IDisposable
    {
        private readonly EvidenceQueueObservation observer;
        private readonly EvidenceServiceStage stage;
        internal EvidenceServiceScope(EvidenceQueueObservation observer, EvidenceServiceStage stage)
        { this.observer = observer; this.stage = stage; observer.BeginStage(stage, EvidenceQueueObservation.Now); }
        public void Dispose() { if (observer != null) observer.EndStage(stage, EvidenceQueueObservation.Now); }
    }

    public sealed partial class EvidenceQueueObservation
    {
        public const int ServiceDetailCapacity = 28, ServiceFirstCount = 8, ServiceSlowestCount = 16;
        public const int ServiceStageCount = 49, ServiceStackDepth = 16, ServiceSourceCapacity = 8;
        public const int ServiceIdentityLength = 40, ServiceRecordStageLength = 32, ServiceSourceLength = 100;
        private const int FlushRangesPerDetail = 2;
        private const int First = 1, Slowest = 2, Linked = 4, Recent = 8, IdentityUnknown = 16,
            StagesUnknown = 32, FlushUnknown = 64, MixedPhases = 128, CpuStartRead = 256, CpuEndRead = 512;
        private ServiceRecord[] serviceRecords;
        private ServiceStageValue[] serviceStages;
        private ServiceFlushRange[] serviceFlushRanges;
        private ServiceSource[] serviceSources;
        private char[] serviceSourceCharacters;
        private ServiceStackEntry[] serviceStack;
        private ServiceBackground[] serviceBackgrounds;
        private ServiceBackgroundStage[] serviceBackgroundStages;
        private EvidenceServiceCpuProbe serviceCpuProbe;
        private long serviceOrdinal, publishedWorkState, pendingServiceQueuedAt, pendingServiceBytes;
        private long rejectedWorkState, rejectedWorkTicks, completedServices, serviceOverflowEvents, serviceLifecycleErrors;
        private int serviceSourceCount, serviceStackCount, suppressedServiceDepth, firstServiceRejectionSet;
        private int serviceContext;
        private bool serviceContextActive, serviceRejectionLinked, serviceRejectionLinksComplete = true;

        private struct ServiceRecord
        {
            public long id, queuedAt, bytes, start, end, cpuStart, cpuEnd, cpuReadTicks, cpuBoundaryTicks, cpuKernelStart, cpuKernelEnd;
            public ulong firstSequence, lastSequence, seenStages;
            public string engine, operation, stage;
            public int source, kind, logicalCount, phase, flags, flushCount, retryCount, ioFailureCount,
                lastHResult, lastAttempt, cpuThread, cpuStatus;
        }
        private struct ServiceStageValue { public long inclusive, exclusive, maxStart, maxEnd; }
        private struct ServiceStackEntry { public long start, childTicks; public int stage; }
        private struct ServiceSource { public uint round; public int runLength, captureLength; }
        private struct ServiceFlushRange { public ulong first, last; public int source, count, kind; }
        private struct ServiceBackground
        {
            public long count, wallTicks, maxStart, maxEnd;
            public ulong seenStages;
            public long ioFailures, retries, flushedBlocks;
            public bool complete;
        }
        private struct ServiceBackgroundStage { public long inclusive, exclusive; }

        // Includes worst-case retained string bodies, source characters, all scratch arrays and a conservative
        // 1,536-byte allowance for array headers, new instance fields and the shared CPU-probe summary.
        public static long ServiceStorageBytes =>
            (long)Marshal.SizeOf<ServiceRecord>() * ServiceDetailCapacity +
            (long)Marshal.SizeOf<ServiceStageValue>() * ServiceDetailCapacity * ServiceStageCount +
            (long)Marshal.SizeOf<ServiceFlushRange>() * ServiceDetailCapacity * FlushRangesPerDetail +
            (long)Marshal.SizeOf<ServiceSource>() * ServiceSourceCapacity +
            (long)sizeof(char) * ServiceSourceCapacity * ServiceSourceLength * 2 +
            (long)Marshal.SizeOf<ServiceStackEntry>() * ServiceStackDepth +
            (long)Marshal.SizeOf<ServiceBackground>() * 3 +
            (long)Marshal.SizeOf<ServiceBackgroundStage>() * 3 * ServiceStageCount +
            (long)ServiceDetailCapacity * (2 * BoundedStringBytes(ServiceIdentityLength) + BoundedStringBytes(ServiceRecordStageLength)) + 1536;
        private static int BoundedStringBytes(int characters) => (24 + (characters + 1) * 2 + 7) & ~7;

        private void InitializeServiceObservation()
        {
            long existingWindows = (long)(Marshal.SizeOf<ProducerWindow>() + Marshal.SizeOf<ConsumerWindow>()) * WindowCount;
            if (existingWindows + (64 << 10) + ServiceStorageBytes > ReservedBytes)
                throw new InvalidOperationException("Service observations exceed the existing diagnostic reservation.");
            serviceRecords = new ServiceRecord[ServiceDetailCapacity];
            serviceStages = new ServiceStageValue[ServiceDetailCapacity * ServiceStageCount];
            serviceFlushRanges = new ServiceFlushRange[ServiceDetailCapacity * FlushRangesPerDetail];
            serviceSources = new ServiceSource[ServiceSourceCapacity];
            serviceSourceCharacters = new char[ServiceSourceCapacity * ServiceSourceLength * 2];
            serviceStack = new ServiceStackEntry[ServiceStackDepth];
            serviceBackgrounds = new ServiceBackground[3];
            serviceBackgroundStages = new ServiceBackgroundStage[3 * ServiceStageCount];
            for (int i = 0; i < serviceBackgrounds.Length; i++) serviceBackgrounds[i].complete = true;
            serviceCpuProbe = EvidenceServiceCpuCounter.Snapshot;
        }

        internal bool HasServiceContext => Active && serviceContextActive;

        // The Store calls this in its existing dequeue gate. IDs derive solely from FIFO work order;
        // no extra field or object is added to each queued item.
        public void PublishDequeuedWork(long queuedAt, long bytes)
        {
            if (!Active) return;
            LinkFirstServiceRejection();
            pendingServiceQueuedAt = queuedAt; pendingServiceBytes = bytes;
            Volatile.Write(ref publishedWorkState, ++serviceOrdinal);
        }

        public void BeginService(long startTicks)
        {
            if (!Active) return;
            StartServiceContext(-1, startTicks);
            ref var record = ref serviceRecords[0];
            record.id = serviceOrdinal; record.queuedAt = pendingServiceQueuedAt; record.bytes = pendingServiceBytes;
            record.kind = (int)EvidenceServiceWorkKind.Schedule;
            if (record.id <= 0) { record.flags |= IdentityUnknown; serviceLifecycleErrors++; }
            record.cpuStatus = (int)(serviceCpuProbe.verified ? EvidenceServiceCpuStatus.Unavailable : EvidenceServiceCpuStatus.Unverified);
            if (serviceCpuProbe.available)
            {
                var sample = EvidenceServiceCpuCounter.Read();
                record.cpuReadTicks = sample.readTicks;
                record.cpuBoundaryTicks = Math.Max(0, Now - startTicks);
                if (sample.available) { record.cpuStart = sample.total100ns; record.cpuKernelStart = sample.kernel100ns; record.cpuThread = sample.threadId; record.flags |= CpuStartRead; }
                else record.cpuStatus = (int)EvidenceServiceCpuStatus.ReadFailed;
            }
            LinkFirstServiceRejection();
        }

        private void StartServiceContext(int context, long startTicks)
        {
            if (serviceContextActive) serviceLifecycleErrors++;
            serviceContext = context; serviceContextActive = true;
            serviceRecords[0] = new ServiceRecord { start = startTicks, source = -1, phase = -1, lastAttempt = -1 };
            Array.Clear(serviceStages, 0, ServiceStageCount);
            Array.Clear(serviceFlushRanges, 0, FlushRangesPerDetail);
            serviceStackCount = 0; suppressedServiceDepth = 0;
            EvidenceServiceTiming.Current = this;
        }

        public void BindServiceIdentity(in EvidenceServiceIdentity identity)
        {
            if (!HasServiceContext || serviceContext != -1) return;
            ref var record = ref serviceRecords[0];
            record.kind = (int)identity.kind; record.firstSequence = identity.firstSequence; record.lastSequence = identity.lastSequence;
            record.logicalCount = identity.logicalCount; record.phase = identity.phase;
            if (identity.mixedPhases) record.flags |= MixedPhases;
            bool unknown = false;
            record.engine = Bounded(identity.engine, ServiceIdentityLength, ref unknown);
            record.operation = Bounded(identity.operation, ServiceIdentityLength, ref unknown);
            record.stage = Bounded(identity.stage, ServiceRecordStageLength, ref unknown);
            record.source = Source(identity.runId, 0, identity.runId?.Length ?? 0, identity.round,
                identity.captureId, 0, identity.captureId?.Length ?? 0);
            if (identity.kind != EvidenceServiceWorkKind.Schedule &&
                (record.source < 0 || identity.firstSequence == 0 || identity.lastSequence < identity.firstSequence || identity.logicalCount <= 0)) unknown = true;
            if (unknown) { record.flags |= IdentityUnknown; serviceOverflowEvents++; }
        }

        public void BeginStage(EvidenceServiceStage stage, long ticks)
        {
            if (!HasServiceContext) return;
            ref var record = ref serviceRecords[0];
            int index = (int)stage;
            if (index < 0 || index >= ServiceStageCount || serviceStackCount == ServiceStackDepth || suppressedServiceDepth != 0)
            { suppressedServiceDepth++; record.flags |= StagesUnknown; serviceOverflowEvents++; return; }
            record.seenStages |= 1UL << index;
            serviceStack[serviceStackCount++] = new ServiceStackEntry { stage = index, start = ticks };
        }

        public void EndStage(EvidenceServiceStage stage, long ticks)
        {
            if (!HasServiceContext) return;
            if (suppressedServiceDepth != 0) { suppressedServiceDepth--; return; }
            ref var record = ref serviceRecords[0];
            if (serviceStackCount == 0 || serviceStack[serviceStackCount - 1].stage != (int)stage)
            { record.flags |= StagesUnknown; serviceLifecycleErrors++; serviceStackCount = 0; return; }
            var frame = serviceStack[--serviceStackCount];
            long duration = ticks - frame.start;
            if (duration < 0 || frame.childTicks > duration)
            { record.flags |= StagesUnknown; serviceLifecycleErrors++; return; }
            ref var value = ref serviceStages[frame.stage];
            value.inclusive += duration; value.exclusive += duration - frame.childTicks;
            if (duration > value.maxEnd - value.maxStart || value.maxStart == 0)
            { value.maxStart = frame.start; value.maxEnd = ticks; }
            if (serviceStackCount != 0) serviceStack[serviceStackCount - 1].childTicks += duration;
        }

        public void RecordIoRetry(int hresult, int attempt = -1, bool willRetry = true)
        {
            if (!HasServiceContext) return;
            ref var record = ref serviceRecords[0];
            record.ioFailureCount++; if (willRetry) record.retryCount++;
            record.lastHResult = hresult; record.lastAttempt = attempt;
        }

        public void RecordFlushedBlock(string runId, uint round, string captureId, ulong first, ulong last,
            int count, EvidenceServiceBlockKind kind)
        {
            if (!HasServiceContext) return;
            RecordFlush(Source(runId, 0, runId?.Length ?? 0, round, captureId, 0, captureId?.Length ?? 0), first, last, count, kind);
        }

        public void RecordFlushedBlockFromDirectory(string directory, ulong first, ulong last, int count, EvidenceServiceBlockKind kind)
        {
            if (!HasServiceContext) return;
            int source = -1;
            if (directory != null)
            {
                int end = directory.Length;
                while (end > 0 && Separator(directory[end - 1])) end--;
                int captureStart = PreviousSeparator(directory, end) + 1;
                int sourcesEnd = captureStart - 1, sourcesStart = PreviousSeparator(directory, sourcesEnd) + 1;
                int roundEnd = sourcesStart - 1, roundStart = PreviousSeparator(directory, roundEnd) + 1;
                int runEnd = roundStart - 1, runStart = PreviousSeparator(directory, runEnd) + 1;
                if (sourcesEnd - sourcesStart == 7 && string.CompareOrdinal(directory, sourcesStart, "sources", 0, 7) == 0 &&
                    runEnd > runStart && TryRound(directory, roundStart, roundEnd, out uint round))
                    source = Source(directory, runStart, runEnd - runStart, round, directory, captureStart, end - captureStart);
            }
            RecordFlush(source, first, last, count, kind);
        }
        private static bool Separator(char value) => value == '/' || value == '\\';
        private static int PreviousSeparator(string text, int end)
        { for (int i = end - 1; i >= 0; i--) if (Separator(text[i])) return i; return -1; }
        private static bool TryRound(string text, int start, int end, out uint round)
        {
            round = 0; if (start < 0 || end <= start) return false;
            for (int i = start; i < end; i++)
            {
                int digit = text[i] - '0';
                if (digit < 0 || digit > 9 || round > (uint.MaxValue - digit) / 10L) return false;
                round = round * 10 + (uint)digit;
            }
            return true;
        }

        private void RecordFlush(int source, ulong first, ulong last, int count, EvidenceServiceBlockKind kind)
        {
            ref var record = ref serviceRecords[0];
            int index = record.flushCount++;
            // Background operations have aggregate counts only. Their unretained ranges are explicitly labelled at export.
            if (serviceContext != -1) return;
            if (index >= FlushRangesPerDetail || source < 0 || first == 0 || last < first || count <= 0)
            { record.flags |= FlushUnknown; serviceOverflowEvents++; return; }
            serviceFlushRanges[index] = new ServiceFlushRange { source = source, first = first, last = last, count = count, kind = (int)kind };
        }

        private int Source(string run, int runStart, int runLength, uint round, string capture, int captureStart, int captureLength)
        {
            if (run == null || capture == null || runLength < 1 || runLength > ServiceSourceLength || captureLength < 1 || captureLength > ServiceSourceLength)
                return -1;
            for (int i = 0; i < serviceSourceCount; i++)
            {
                var source = serviceSources[i];
                if (source.round == round && source.runLength == runLength && source.captureLength == captureLength &&
                    SourceEquals(i * ServiceSourceLength * 2, run, runStart, runLength) &&
                    SourceEquals(i * ServiceSourceLength * 2 + ServiceSourceLength, capture, captureStart, captureLength)) return i;
            }
            if (serviceSourceCount == ServiceSourceCapacity) return -1;
            int slot = serviceSourceCount++;
            serviceSources[slot] = new ServiceSource { round = round, runLength = runLength, captureLength = captureLength };
            int offset = slot * ServiceSourceLength * 2;
            for (int i = 0; i < runLength; i++) serviceSourceCharacters[offset + i] = run[runStart + i];
            for (int i = 0; i < captureLength; i++) serviceSourceCharacters[offset + ServiceSourceLength + i] = capture[captureStart + i];
            return slot;
        }
        private bool SourceEquals(int offset, string value, int start, int length)
        { for (int i = 0; i < length; i++) if (serviceSourceCharacters[offset + i] != value[start + i]) return false; return true; }

        // Producer holds the existing Store gate. Only one atomic signed ordinal is read; never consumer detail buffers.
        private void CaptureServiceRejection(long ticks)
        {
            rejectedWorkState = Volatile.Read(ref publishedWorkState); rejectedWorkTicks = ticks;
            Volatile.Write(ref firstServiceRejectionSet, 1);
        }
        private void LinkFirstServiceRejection()
        {
            if (serviceRejectionLinked || Volatile.Read(ref firstServiceRejectionSet) == 0) return;
            long newest = Math.Abs(rejectedWorkState);
            for (long id = newest; id > 0 && id > newest - 2; id--)
            {
                bool found = false;
                for (int slot = 0; slot < ServiceDetailCapacity; slot++) if (serviceRecords[slot].id == id)
                { serviceRecords[slot].flags |= Linked; found = true; }
                if (!found) serviceRejectionLinksComplete = false;
            }
            serviceRejectionLinked = true;
            for (int slot = 1; slot < ServiceDetailCapacity; slot++)
            {
                serviceRecords[slot].flags &= ~Recent;
                if ((serviceRecords[slot].flags & (First | Slowest | Linked)) == 0) serviceRecords[slot] = default;
            }
        }

        public void EndService(long endTicks)
        {
            if (!HasServiceContext || serviceContext != -1) return;
            FinishServiceContext(endTicks);
            ref var record = ref serviceRecords[0];
            if (serviceCpuProbe.available)
            {
                var sample = EvidenceServiceCpuCounter.Read(); record.cpuReadTicks += sample.readTicks;
                record.cpuBoundaryTicks += Math.Max(0, Now - endTicks);
                if (sample.available) { record.cpuEnd = sample.total100ns; record.cpuKernelEnd = sample.kernel100ns; record.flags |= CpuEndRead; }
                record.cpuStatus = (int)EvidenceServiceCpuCounter.Classify(serviceCpuProbe,
                    (record.flags & CpuStartRead) != 0, sample.available, record.cpuThread == sample.threadId, record.cpuStart, record.cpuEnd,
                    record.end - record.start);
                if ((record.flags & (CpuStartRead | CpuEndRead)) == (CpuStartRead | CpuEndRead) &&
                    (record.cpuKernelEnd < record.cpuKernelStart || record.cpuEnd - record.cpuKernelEnd < record.cpuStart - record.cpuKernelStart))
                    record.cpuStatus = (int)EvidenceServiceCpuStatus.CounterReversed;
            }
            completedServices++;
            RetainCompletedService();
            // Release publication occurs only after lease release, terminal gate and Memory.Release have finished.
            Volatile.Write(ref publishedWorkState, -record.id);
            LinkFirstServiceRejection();
            serviceRecords[0] = default;
            ClearServiceContext();
        }

        private void FinishServiceContext(long endTicks)
        {
            ref var record = ref serviceRecords[0]; record.end = endTicks;
            long exclusive = 0;
            for (int i = 0; i < ServiceStageCount; i++) exclusive += serviceStages[i].exclusive;
            if (serviceStackCount != 0 || suppressedServiceDepth != 0 || endTicks < record.start || exclusive > endTicks - record.start)
            { record.flags |= StagesUnknown; serviceLifecycleErrors++; }
        }
        private void ClearServiceContext()
        { serviceContextActive = false; serviceStackCount = 0; suppressedServiceDepth = 0; if (ReferenceEquals(EvidenceServiceTiming.Current, this)) EvidenceServiceTiming.Current = null; }

        private void RetainCompletedService()
        {
            LinkFirstServiceRejection();
            ref var current = ref serviceRecords[0];
            if (current.id <= ServiceFirstCount) current.flags |= First;
            int topCount = 0, smallest = -1;
            for (int i = 1; i < ServiceDetailCapacity; i++)
            {
                ref var record = ref serviceRecords[i];
                if ((record.flags & Slowest) != 0)
                {
                    topCount++;
                    if (smallest < 0 || record.end - record.start < serviceRecords[smallest].end - serviceRecords[smallest].start ||
                        (record.end - record.start == serviceRecords[smallest].end - serviceRecords[smallest].start && record.id > serviceRecords[smallest].id)) smallest = i;
                }
                // Three recent items cover publication/latching races; exported first-rejection links still contain at most two items.
                if (serviceRejectionLinked || record.id < current.id - 2) record.flags &= ~Recent;
            }
            if (topCount < ServiceSlowestCount) current.flags |= Slowest;
            else if (current.end - current.start > serviceRecords[smallest].end - serviceRecords[smallest].start)
            { serviceRecords[smallest].flags &= ~Slowest; current.flags |= Slowest; }
            if (!serviceRejectionLinked) current.flags |= Recent;
            int free = -1;
            for (int i = 1; i < ServiceDetailCapacity; i++) if ((serviceRecords[i].flags & (First | Slowest | Linked | Recent)) == 0)
            { serviceRecords[i] = default; if (free < 0) free = i; }
            if ((current.flags & (First | Slowest | Linked | Recent)) == 0) return;
            if (free < 0) { serviceOverflowEvents++; return; }
            serviceRecords[free] = current;
            Array.Copy(serviceStages, 0, serviceStages, free * ServiceStageCount, ServiceStageCount);
            Array.Copy(serviceFlushRanges, 0, serviceFlushRanges, free * FlushRangesPerDetail, FlushRangesPerDetail);
        }

        public void BeginBackground(EvidenceServiceContext context, long startTicks)
        {
            if (!Active) return;
            if (serviceContextActive || (int)context < 0 || (int)context >= 3) { serviceLifecycleErrors++; return; }
            StartServiceContext((int)context, startTicks);
        }
        public void EndBackground(long endTicks)
        {
            if (!HasServiceContext || serviceContext < 0) return;
            FinishServiceContext(endTicks);
            var record = serviceRecords[0]; ref var background = ref serviceBackgrounds[serviceContext];
            background.count++; background.wallTicks += Math.Max(0, record.end - record.start); background.seenStages |= record.seenStages;
            background.ioFailures += record.ioFailureCount; background.retries += record.retryCount; background.flushedBlocks += record.flushCount;
            if ((record.flags & StagesUnknown) != 0) background.complete = false;
            if (record.end - record.start > background.maxEnd - background.maxStart) { background.maxStart = record.start; background.maxEnd = record.end; }
            for (int i = 0; i < ServiceStageCount; i++)
            {
                ref var target = ref serviceBackgroundStages[serviceContext * ServiceStageCount + i];
                target.inclusive += serviceStages[i].inclusive; target.exclusive += serviceStages[i].exclusive;
            }
            serviceRecords[0] = default; ClearServiceContext();
        }

        private object ExportServiceSource(int index)
        {
            if (index < 0 || index >= serviceSourceCount) return null;
            var value = serviceSources[index]; int offset = index * ServiceSourceLength * 2;
            return new { runId = new string(serviceSourceCharacters, offset, value.runLength), round = value.round,
                captureId = new string(serviceSourceCharacters, offset + ServiceSourceLength, value.captureLength) };
        }

        private void WriteServiceObservation(JsonWriter writer, JsonSerializer serializer)
        {
            LinkFirstServiceRejection();
            writer.WritePropertyName("serviceDetails"); writer.WriteStartObject();
            Property(writer, serializer, "schemaVersion", 1);
            Property(writer, serializer, "clock", "Stopwatch; same origin and frequency as queue windows");
            Property(writer, serializer, "threadScope", "Store writer only; replication ImportBlock I/O on another thread is unmeasured");
            Property(writer, serializer, "selection", "first 8 + slowest 16 + first rejection active/last completed and previous; 3 recent race-protection slots");
            Property(writer, serializer, "capacity", ServiceDetailCapacity); Property(writer, serializer, "storageBytes", ServiceStorageBytes);
            Property(writer, serializer, "cpuProbe", serviceCpuProbe);
            Property(writer, serializer, "overflowEvents", serviceOverflowEvents); Property(writer, serializer, "lifecycleErrors", serviceLifecycleErrors);
            Property(writer, serializer, "completedWorkItems", completedServices);
            bool complete = !serviceContextActive && serviceOverflowEvents == 0 && serviceLifecycleErrors == 0 && serviceRejectionLinksComplete &&
                completedServices == consumerTotals.workCompleted;
            int retained = 0;
            for (int i = 1; i < ServiceDetailCapacity; i++) if (serviceRecords[i].id != 0)
            { retained++; if ((serviceRecords[i].flags & (IdentityUnknown | StagesUnknown | FlushUnknown)) != 0) complete = false; }
            Property(writer, serializer, "complete", complete);
            Property(writer, serializer, "retainedWorkItems", retained);
            Property(writer, serializer, "unretainedWorkItems", completedServices - retained);
            Property(writer, serializer, "unretainedIsOverflow", false);
            Property(writer, serializer, "firstRejectionWork", Volatile.Read(ref firstServiceRejectionSet) == 0 ? null : new {
                ticks = rejectedWorkTicks, activeWorkId = rejectedWorkState > 0 ? (long?)rejectedWorkState : null,
                precedingWorkIds = rejectedWorkState > 0 ? (rejectedWorkState > 1 ? new[] { rejectedWorkState - 1 } : Array.Empty<long>()) :
                    (rejectedWorkState < -1 ? new[] { -rejectedWorkState, -rejectedWorkState - 1 } : rejectedWorkState == -1 ? new[] { 1L } : Array.Empty<long>()),
                linksComplete = serviceRejectionLinksComplete });
            writer.WritePropertyName("workItems"); writer.WriteStartArray();
            // Selection is fixed-size; sort only during stopped export without allocating a per-work collection.
            long previous = 0;
            for (int item = 0; item < retained; item++)
            {
                int slot = -1;
                for (int i = 1; i < ServiceDetailCapacity; i++) if (serviceRecords[i].id > previous && (slot < 0 || serviceRecords[i].id < serviceRecords[slot].id)) slot = i;
                if (slot < 0) break;
                WriteServiceRecord(writer, serializer, slot); previous = serviceRecords[slot].id;
            }
            writer.WriteEndArray();
            writer.WritePropertyName("background"); writer.WriteStartArray();
            for (int i = 0; i < 3; i++)
            {
                var background = serviceBackgrounds[i]; if (background.count == 0) continue;
                writer.WriteStartObject(); Property(writer, serializer, "context", (EvidenceServiceContext)i);
                Property(writer, serializer, "count", background.count); Property(writer, serializer, "wallTicks", background.wallTicks);
                Property(writer, serializer, "maxStartTicks", background.maxStart); Property(writer, serializer, "maxEndTicks", background.maxEnd);
                Property(writer, serializer, "complete", background.complete); Property(writer, serializer, "ioFailureCount", background.ioFailures);
                Property(writer, serializer, "retryCount", background.retries); Property(writer, serializer, "flushedBlockCount", background.flushedBlocks);
                Property(writer, serializer, "flushedBlockRanges", "not retained in aggregate background contexts");
                writer.WritePropertyName("stages"); writer.WriteStartArray();
                for (int stage = 0; stage < ServiceStageCount; stage++) if ((background.seenStages & (1UL << stage)) != 0)
                {
                    var value = serviceBackgroundStages[i * ServiceStageCount + stage];
                    serializer.Serialize(writer, new { stage = (EvidenceServiceStage)stage, inclusiveTicks = value.inclusive, exclusiveTicks = value.exclusive });
                }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }

        private void WriteServiceRecord(JsonWriter writer, JsonSerializer serializer, int slot)
        {
            var record = serviceRecords[slot]; long exclusive = 0;
            for (int i = 0; i < ServiceStageCount; i++) exclusive += serviceStages[slot * ServiceStageCount + i].exclusive;
            writer.WriteStartObject();
            Property(writer, serializer, "workId", record.id); Property(writer, serializer, "kind", (EvidenceServiceWorkKind)record.kind);
            Property(writer, serializer, "source", ExportServiceSource(record.source)); Property(writer, serializer, "engine", record.engine);
            Property(writer, serializer, "operation", record.operation); Property(writer, serializer, "stage", record.stage);
            Property(writer, serializer, "firstSequence", record.firstSequence == 0 ? (ulong?)null : record.firstSequence);
            Property(writer, serializer, "lastSequence", record.lastSequence == 0 ? (ulong?)null : record.lastSequence);
            Property(writer, serializer, "logicalCount", record.logicalCount);
            Property(writer, serializer, "phase", record.phase < 0 ? (int?)null : record.phase); Property(writer, serializer, "mixedPhases", (record.flags & MixedPhases) != 0);
            Property(writer, serializer, "queuedAtTicks", record.queuedAt); Property(writer, serializer, "chargedBytes", record.bytes);
            Property(writer, serializer, "startedTicks", record.start); Property(writer, serializer, "endedTicks", record.end);
            Property(writer, serializer, "wallTicks", record.end - record.start);
            Property(writer, serializer, "wallResidualTicks", (record.flags & StagesUnknown) == 0 ? (long?)(record.end - record.start - exclusive) : null);
            Property(writer, serializer, "identityComplete", (record.flags & IdentityUnknown) == 0);
            Property(writer, serializer, "stagesComplete", (record.flags & StagesUnknown) == 0);
            Property(writer, serializer, "flushedBlocksComplete", (record.flags & FlushUnknown) == 0);
            Property(writer, serializer, "flushedBlockCount", record.flushCount);
            Property(writer, serializer, "ioFailureCount", record.ioFailureCount); Property(writer, serializer, "retryCount", record.retryCount);
            Property(writer, serializer, "lastRetryHResult", record.ioFailureCount == 0 ? (int?)null : record.lastHResult);
            Property(writer, serializer, "lastRetryAttempt", record.ioFailureCount == 0 ? (int?)null : record.lastAttempt);
            Property(writer, serializer, "retention", new { first = (record.flags & First) != 0, slowest = (record.flags & Slowest) != 0,
                firstRejectionLinked = (record.flags & Linked) != 0, recent = (record.flags & Recent) != 0 });
            Property(writer, serializer, "cpu", new { status = (EvidenceServiceCpuStatus)record.cpuStatus,
                total100ns = record.cpuStatus == (int)EvidenceServiceCpuStatus.Measured ? (long?)(record.cpuEnd - record.cpuStart) : null,
                kernel100ns = record.cpuStatus == (int)EvidenceServiceCpuStatus.Measured ? (long?)(record.cpuKernelEnd - record.cpuKernelStart) : null,
                user100ns = record.cpuStatus == (int)EvidenceServiceCpuStatus.Measured ? (long?)(record.cpuEnd - record.cpuStart - record.cpuKernelEnd + record.cpuKernelStart) : null,
                rawDelta100ns = (record.flags & (CpuStartRead | CpuEndRead)) == (CpuStartRead | CpuEndRead) ? (long?)(record.cpuEnd - record.cpuStart) : null,
                rawKernelDelta100ns = (record.flags & (CpuStartRead | CpuEndRead)) == (CpuStartRead | CpuEndRead) ? (long?)(record.cpuKernelEnd - record.cpuKernelStart) : null,
                rawUserDelta100ns = (record.flags & (CpuStartRead | CpuEndRead)) == (CpuStartRead | CpuEndRead) ? (long?)(record.cpuEnd - record.cpuStart - record.cpuKernelEnd + record.cpuKernelStart) : null,
                boundaryReadTicks = record.cpuReadTicks, boundarySkewTicks = record.cpuBoundaryTicks,
                quantizationErrorEstimate100ns = serviceCpuProbe.available ? (long?)(2 * serviceCpuProbe.observedIncrement100ns) : null,
                totalErrorEstimate100ns = serviceCpuProbe.available ? (long?)(2 * serviceCpuProbe.observedIncrement100ns +
                    (long)Math.Ceiling(record.cpuBoundaryTicks * (10000000d / Stopwatch.Frequency))) : null,
                errorEstimateIsEmpirical = true,
                errorEstimateBasis = "two observed clock increments plus both sampled boundary skews; conservative probe-based estimate, not an OS guarantee",
                attribution = "boundary reads; wall-minus-CPU is unclassified, not a GC or disk measurement" });
            writer.WritePropertyName("stages"); writer.WriteStartArray();
            for (int i = 0; i < ServiceStageCount; i++) if ((record.seenStages & (1UL << i)) != 0)
            {
                var stage = serviceStages[slot * ServiceStageCount + i];
                serializer.Serialize(writer, new { stage = (EvidenceServiceStage)i, inclusiveTicks = stage.inclusive, exclusiveTicks = stage.exclusive,
                    maxStartTicks = stage.maxStart, maxEndTicks = stage.maxEnd });
            }
            writer.WriteEndArray(); writer.WritePropertyName("flushedBlocks"); writer.WriteStartArray();
            for (int i = 0; i < Math.Min(record.flushCount, FlushRangesPerDetail); i++)
            {
                var range = serviceFlushRanges[slot * FlushRangesPerDetail + i];
                if (range.count <= 0) continue;
                serializer.Serialize(writer, new { kind = (EvidenceServiceBlockKind)range.kind, source = ExportServiceSource(range.source),
                    firstSequence = range.first, lastSequence = range.last, recordCount = range.count });
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }

        private void ReleaseServiceObservation()
        {
            serviceRecords = null; serviceStages = null; serviceFlushRanges = null; serviceSources = null;
            serviceSourceCharacters = null; serviceStack = null; serviceBackgrounds = null; serviceBackgroundStages = null; serviceCpuProbe = null;
            if (ReferenceEquals(EvidenceServiceTiming.Current, this)) EvidenceServiceTiming.Current = null;
        }
    }

    public enum EvidenceServiceCpuStatus { Unverified, Unavailable, ReadFailed, ThreadMismatch, CounterReversed, BelowObservedResolution, Measured }
    public struct EvidenceServiceCpuSample
    {
        public bool available;
        public long total100ns, user100ns, kernel100ns, readTicks;
        public int threadId, nativeError;
    }
    public sealed class EvidenceServiceCpuProbe
    {
        public readonly bool verified, available;
        public readonly string reason;
        public readonly long observedIncrement100ns, busyCpu100ns, sleepCpu100ns, maxReadTicks, wallTicks;
        public readonly int positiveIncrements, reads, threadId, processId;
        internal EvidenceServiceCpuProbe(bool verified, bool available, string reason, long increment = 0, long busy = 0,
            long sleep = 0, long maxRead = 0, long wall = 0, int positives = 0, int reads = 0, int thread = 0, int process = 0)
        {
            this.verified = verified; this.available = available; this.reason = reason; observedIncrement100ns = increment;
            busyCpu100ns = busy; sleepCpu100ns = sleep; maxReadTicks = maxRead; wallTicks = wall;
            positiveIncrements = positives; this.reads = reads; threadId = thread; processId = process;
        }
    }
    public static class EvidenceServiceCpuCounter
    {
        private static readonly object probeGate = new();
        private static readonly bool Windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static EvidenceServiceCpuProbe probe = new(false, false, "NotProbedInThisProcess");
        private static bool probeTimedOut;
        public static EvidenceServiceCpuProbe Snapshot => Volatile.Read(ref probe);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);

        public static EvidenceServiceCpuSample Read()
        {
            var result = new EvidenceServiceCpuSample { threadId = Environment.CurrentManagedThreadId };
            if (!Windows) return result;
            long start = Stopwatch.GetTimestamp();
            try
            {
                result.available = GetThreadTimes(new IntPtr(-2), out _, out _, out long kernel, out long user);
                if (result.available) { result.kernel100ns = kernel; result.user100ns = user; result.total100ns = kernel + user; }
                else result.nativeError = Marshal.GetLastWin32Error();
            }
            catch (DllNotFoundException) { result.available = false; }
            catch (EntryPointNotFoundException) { result.available = false; }
            result.readTicks = Stopwatch.GetTimestamp() - start;
            return result;
        }

        // Explicit preflight only: callers invoke this before creating any Store and before the shared origin.
        // Calibration runs on an independent short-lived thread, never on the writer whose startup is measured.
        public static EvidenceServiceCpuProbe Probe()
        {
            lock (probeGate)
            {
                if (probeTimedOut) throw new TimeoutException("The CPU preflight thread did not join; this process cannot start a measured workload.");
                if (probe.verified) return probe;
                if (!Windows) return probe = new EvidenceServiceCpuProbe(true, false, "PlatformUnsupported");
                EvidenceServiceCpuProbe result = null;
                var worker = new Thread(() => { try { result = RunProbe(); } catch { result = new EvidenceServiceCpuProbe(true, false, "ProbeFailed"); } })
                    { IsBackground = true, Name = "Evidence CPU clock preflight" };
                worker.Start();
                if (!worker.Join(2000))
                {
                    probeTimedOut = true;
                    Volatile.Write(ref probe, new EvidenceServiceCpuProbe(true, false, "ProbeTimeout"));
                    throw new TimeoutException("The CPU preflight thread did not join; this process cannot start a measured workload.");
                }
                Volatile.Write(ref probe, result ?? new EvidenceServiceCpuProbe(true, false, "ProbeUnavailable"));
                return probe;
            }
        }
        private static EvidenceServiceCpuProbe RunProbe()
        {
            long started = Stopwatch.GetTimestamp(); var beforeSleep = Read();
            if (!beforeSleep.available) return new EvidenceServiceCpuProbe(true, false, "NativeReadFailed");
            Thread.Sleep(10); var afterSleep = Read();
            if (!afterSleep.available) return new EvidenceServiceCpuProbe(true, false, "NativeReadFailed");
            if (afterSleep.threadId != beforeSleep.threadId || afterSleep.total100ns < beforeSleep.total100ns)
                return new EvidenceServiceCpuProbe(true, false, "UnstableNativeCounter");
            long interval = Math.Max(1, Stopwatch.Frequency / 1000), until = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 60 / 1000;
            long next = Stopwatch.GetTimestamp(), smallest = long.MaxValue, maximumRead = Math.Max(beforeSleep.readTicks, afterSleep.readTicks);
            int positives = 0, reads = 2; var previous = afterSleep;
            while (Stopwatch.GetTimestamp() < until)
            {
                if (Stopwatch.GetTimestamp() < next) continue;
                var sample = Read(); reads++; maximumRead = Math.Max(maximumRead, sample.readTicks);
                if (!sample.available || sample.threadId != previous.threadId || sample.total100ns < previous.total100ns)
                    return new EvidenceServiceCpuProbe(true, false, "UnstableNativeCounter");
                long delta = sample.total100ns - previous.total100ns;
                if (delta > 0) { positives++; smallest = Math.Min(smallest, delta); }
                previous = sample; next += interval;
            }
            long busy = previous.total100ns - afterSleep.total100ns, sleep = afterSleep.total100ns - beforeSleep.total100ns;
            bool valid = positives >= 2 && busy > sleep && smallest != long.MaxValue;
            using var process = Process.GetCurrentProcess();
            return new EvidenceServiceCpuProbe(true, valid, valid ? "IndependentThreadBusyAndSleepProbe" : "NoReliablePositiveIncrement",
                valid ? smallest : 0, busy, sleep, maximumRead, Stopwatch.GetTimestamp() - started, positives, reads, previous.threadId, process.Id);
        }
        public static EvidenceServiceCpuStatus Classify(EvidenceServiceCpuProbe probe, bool startRead, bool endRead,
            bool sameThread, long start100ns, long end100ns, long wallStopwatchTicks = long.MaxValue)
        {
            if (probe == null || !probe.verified) return EvidenceServiceCpuStatus.Unverified;
            if (!probe.available || probe.observedIncrement100ns <= 0) return EvidenceServiceCpuStatus.Unavailable;
            if (!startRead || !endRead) return EvidenceServiceCpuStatus.ReadFailed;
            if (!sameThread) return EvidenceServiceCpuStatus.ThreadMismatch;
            if (end100ns < start100ns) return EvidenceServiceCpuStatus.CounterReversed;
            if (end100ns - start100ns < probe.observedIncrement100ns ||
                (wallStopwatchTicks != long.MaxValue && wallStopwatchTicks * (10000000d / Stopwatch.Frequency) < probe.observedIncrement100ns))
                return EvidenceServiceCpuStatus.BelowObservedResolution;
            return EvidenceServiceCpuStatus.Measured;
        }
    }
}
