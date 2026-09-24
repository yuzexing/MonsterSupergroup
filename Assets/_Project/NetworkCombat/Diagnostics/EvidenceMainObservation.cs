using System;
using System.Runtime.InteropServices;
using System.Threading;
using MonsterSupergroup.GAS;
using Newtonsoft.Json;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    public sealed partial class EvidenceQueueObservation : IDiagnosticMainTiming
    {
        public const int MainFrameCapacity = 36, MainStageCount = 16, MainStackDepth = 16;
        private const int MainSlots = MainFrameCapacity + 1;
        private const int MainStagesUnknown = 1, MainSequenceUnknown = 2, MainWallUnknown = 4, MainIdentityUnknown = 8;
        private MainFrameRecord[] mainRecords;
        private MainStageValue[] mainStages;
        private MainStackEntry[] mainStack;
        private int[] mainStageCalls;
        private int mainFrameState, mainThread, mainStackCount, mainSuppressedDepth, mainRetained;
        private long mainFrameCount, mainInvalidFrames, mainOverflowEvents, mainLifecycleErrors, mainThreadViolations, mainLastTicks;
        private long mainStageCallCount;

        private struct MainFrameRecord
        {
            public long start, end;
            public ulong before, after;
            public int frame, flags;
            public ulong seenStages;
        }
        private struct MainStageValue { public long inclusive, exclusive, maximum; }
        private struct MainStackEntry { public long start, childTicks; public int stage; }

        // Includes all new scalar fields and ambient bridge state within a 1 KiB allowance;
        // the call-count array additionally has its own conservative 32-byte header allowance.
        public static long MainStorageBytes =>
            (long)Marshal.SizeOf<MainFrameRecord>() * MainSlots +
            (long)Marshal.SizeOf<MainStageValue>() * MainSlots * MainStageCount +
            (long)Marshal.SizeOf<MainStackEntry>() * MainStackDepth + 1024 +
            (long)sizeof(int) * MainSlots * MainStageCount + 32;
        private static long ObservationWindowStorageBytes =>
            (long)(Marshal.SizeOf<ProducerWindow>() + Marshal.SizeOf<ConsumerWindow>()) * WindowCount;
        public static long AccountedStorageBytes => ObservationWindowStorageBytes + (64 << 10) + ServiceStorageBytes + MainStorageBytes;

        private void InitializeMainObservation()
        {
            if (AccountedStorageBytes > ReservedBytes)
                throw new InvalidOperationException("Main observations exceed the existing diagnostic reservation.");
            mainRecords = new MainFrameRecord[MainSlots];
            mainStages = new MainStageValue[MainSlots * MainStageCount];
            mainStack = new MainStackEntry[MainStackDepth];
            mainStageCalls = new int[MainSlots * MainStageCount];
        }

        public void BeginMainFrame(int frame, long startedTicks, ulong sequenceBefore)
        {
            if (!Active) return;
            int previous = Interlocked.CompareExchange(ref mainFrameState, 1, 0);
            if (previous != 0)
            {
                if (previous > 0) Interlocked.Increment(ref mainLifecycleErrors);
                return;
            }
            bool attached = false;
            try
            {
                if (!DiagnosticMainTiming.TryAttach(this)) { mainLifecycleErrors++; return; }
                attached = true;
                mainThread = Environment.CurrentManagedThreadId;
                mainStackCount = mainSuppressedDepth = 0; mainLastTicks = startedTicks;
                Array.Clear(mainStages, 0, MainStageCount);
                Array.Clear(mainStageCalls, 0, MainStageCount);
                mainRecords[0] = new MainFrameRecord { frame = frame, start = startedTicks, before = sequenceBefore,
                    flags = (frame < 0 ? MainIdentityUnknown : 0) | (startedTicks < 0 ? MainWallUnknown : 0) };
            }
            catch { Interlocked.Increment(ref mainLifecycleErrors); attached = false; }
            finally
            {
                if (!attached)
                {
                    DiagnosticMainTiming.Detach(this); mainThread = 0;
                    Volatile.Write(ref mainFrameState, 0);
                }
            }
        }

        private bool OnMainFrameThread()
        {
            if (!Active || Volatile.Read(ref mainFrameState) != 1) return false;
            if (Environment.CurrentManagedThreadId == mainThread) return true;
            Interlocked.Increment(ref mainThreadViolations); return false;
        }

        private void MarkMainStageUnknown() { mainRecords[0].flags |= MainStagesUnknown; }

        public void BeginMainStage(DiagnosticMainStage stage, long ticks)
        {
            if (!OnMainFrameThread()) return;
            try
            {
                int index = (int)stage;
                if (index >= 0 && index < MainStageCount)
                {
                    mainStageCallCount++;
                    mainRecords[0].seenStages |= 1UL << index;
                    if (mainStageCalls[index] < int.MaxValue) mainStageCalls[index]++;
                    else { MarkMainStageUnknown(); mainOverflowEvents++; }
                }
                if (mainSuppressedDepth != 0 || mainStackCount == MainStackDepth || index < 0 || index >= MainStageCount || ticks < mainLastTicks)
                {
                    MarkMainStageUnknown(); mainSuppressedDepth++;
                    if (mainStackCount == MainStackDepth) mainOverflowEvents++;
                    return;
                }
                mainLastTicks = ticks;
                mainStack[mainStackCount++] = new MainStackEntry { stage = index, start = ticks };
            }
            catch { mainLifecycleErrors++; MarkMainStageUnknown(); }
        }

        public void EndMainStage(DiagnosticMainStage stage, long ticks)
        {
            if (!OnMainFrameThread()) return;
            try
            {
                if (mainSuppressedDepth != 0) { mainSuppressedDepth--; return; }
                if (mainStackCount == 0 || mainStack[mainStackCount - 1].stage != (int)stage || ticks < mainLastTicks)
                { MarkMainStageUnknown(); mainStackCount = 0; return; }
                mainLastTicks = ticks;
                var entry = mainStack[--mainStackCount];
                long duration = ticks - entry.start;
                if (duration < 0 || entry.childTicks > duration) { MarkMainStageUnknown(); return; }
                ref var value = ref mainStages[(int)stage];
                value.inclusive += duration; value.exclusive += duration - entry.childTicks;
                value.maximum = Math.Max(value.maximum, duration);
                if (mainStackCount > 0) mainStack[mainStackCount - 1].childTicks += duration;
            }
            catch { mainLifecycleErrors++; MarkMainStageUnknown(); }
        }

        public void EndMainFrame(long endedTicks, ulong sequenceAfter)
        {
            if (!OnMainFrameThread()) return;
            try
            {
                ref var record = ref mainRecords[0]; record.end = endedTicks; record.after = sequenceAfter;
                if (sequenceAfter < record.before) record.flags |= MainSequenceUnknown;
                if (endedTicks < record.start || record.start < 0) record.flags |= MainWallUnknown;
                if (mainStackCount != 0 || mainSuppressedDepth != 0 || mainLastTicks > endedTicks || DiagnosticMainTiming.HasFault(this))
                    record.flags |= MainStagesUnknown;
                long exclusive = 0;
                for (int i = 0; i < MainStageCount; i++) exclusive += mainStages[i].exclusive;
                if ((record.flags & MainWallUnknown) != 0 || exclusive > record.end - record.start) record.flags |= MainStagesUnknown;
                mainFrameCount++;
                if (record.flags != 0) mainInvalidFrames++;
                RetainMainFrame();
            }
            catch { mainLifecycleErrors++; }
            finally
            {
                DiagnosticMainTiming.Detach(this);
                mainStackCount = mainSuppressedDepth = 0; mainThread = 0;
                Volatile.Write(ref mainFrameState, 0);
            }
        }

        private static long MainWall(MainFrameRecord record) => (record.flags & MainWallUnknown) != 0 ? 0 : record.end - record.start;
        private static bool MainLonger(MainFrameRecord a, MainFrameRecord b)
        {
            long first = MainWall(a), second = MainWall(b);
            return first > second || first == second && a.frame < b.frame;
        }
        private void RetainMainFrame()
        {
            int slot;
            if (mainRetained < MainFrameCapacity) slot = ++mainRetained;
            else
            {
                slot = 1;
                for (int i = 2; i <= MainFrameCapacity; i++) if (MainLonger(mainRecords[slot], mainRecords[i])) slot = i;
                if (!MainLonger(mainRecords[0], mainRecords[slot])) return;
            }
            mainRecords[slot] = mainRecords[0];
            Array.Copy(mainStages, 0, mainStages, slot * MainStageCount, MainStageCount);
            Array.Copy(mainStageCalls, 0, mainStageCalls, slot * MainStageCount, MainStageCount);
        }

        // Called before any export or release. Closing state also prevents a concurrent BeginMainFrame.
        private bool TryStopMainObservation()
        {
            int state = Interlocked.CompareExchange(ref mainFrameState, -1, 0);
            return state != 1;
        }

        private void WriteMainObservation(JsonWriter writer, JsonSerializer serializer)
        {
            writer.WritePropertyName("mainFrames"); writer.WriteStartObject();
            bool complete = mainFrameCount > 0 && mainInvalidFrames == 0 && mainOverflowEvents == 0 && mainLifecycleErrors == 0 && mainThreadViolations == 0;
            Property(writer, serializer, "enabled", true);
            Property(writer, serializer, "status", mainFrameCount == 0 ? "NotCaptured" : complete ? "Captured" : "Incomplete");
            Property(writer, serializer, "complete", complete); Property(writer, serializer, "capacity", MainFrameCapacity);
            Property(writer, serializer, "storageBytes", MainStorageBytes); Property(writer, serializer, "frameCount", mainFrameCount);
            Property(writer, serializer, "stageCallCount", mainStageCallCount);
            Property(writer, serializer, "unretainedFrames", mainFrameCount - mainRetained);
            Property(writer, serializer, "overflowEvents", mainOverflowEvents); Property(writer, serializer, "invalidFrames", mainInvalidFrames);
            Property(writer, serializer, "lifecycleErrors", mainLifecycleErrors); Property(writer, serializer, "threadViolations", mainThreadViolations);
            Property(writer, serializer, "selection", "longest completed frames; equal durations prefer smaller frame; excludes current frame");
            Property(writer, serializer, "sequenceSemantics", "produced-sequence boundaries, not accepted records");
            Property(writer, serializer, "attribution", "exclusive durations only are additive; WorkloadStep exclusive is NonDiagnosticResidual; maxTicks has no span timestamps");
            writer.WritePropertyName("frames"); writer.WriteStartArray();
            // A stopped export may scan the fixed slots; no per-frame collection or hot-path sorting.
            int previousFrame = int.MinValue, previousSlot = 0;
            for (int item = 0; item < mainRetained; item++)
            {
                int selected = -1;
                for (int slot = 1; slot <= mainRetained; slot++)
                {
                    int frame = mainRecords[slot].frame;
                    if (frame < previousFrame || frame == previousFrame && slot <= previousSlot) continue;
                    if (selected < 0 || frame < mainRecords[selected].frame || frame == mainRecords[selected].frame && slot < selected) selected = slot;
                }
                if (selected < 0) break;
                WriteMainFrame(writer, serializer, selected);
                previousFrame = mainRecords[selected].frame; previousSlot = selected;
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }

        private void WriteMainFrame(JsonWriter writer, JsonSerializer serializer, int slot)
        {
            var record = mainRecords[slot]; long exclusive = 0;
            for (int i = 0; i < MainStageCount; i++) exclusive += mainStages[slot * MainStageCount + i].exclusive;
            bool wallKnown = (record.flags & MainWallUnknown) == 0, stagesKnown = (record.flags & MainStagesUnknown) == 0;
            writer.WriteStartObject(); Property(writer, serializer, "frame", record.frame);
            Property(writer, serializer, "startTicks", record.start); Property(writer, serializer, "endTicks", record.end);
            Property(writer, serializer, "wallTicks", wallKnown ? (long?)(record.end - record.start) : null);
            Property(writer, serializer, "sequenceBefore", record.before); Property(writer, serializer, "sequenceAfter", record.after);
            Property(writer, serializer, "sequenceComplete", (record.flags & MainSequenceUnknown) == 0);
            Property(writer, serializer, "identityComplete", (record.flags & MainIdentityUnknown) == 0);
            Property(writer, serializer, "stagesComplete", stagesKnown);
            Property(writer, serializer, "wallResidualTicks", wallKnown && stagesKnown ? (long?)(record.end - record.start - exclusive) : null);
            writer.WritePropertyName("stages"); writer.WriteStartArray();
            for (int i = 0; i < MainStageCount; i++) if ((record.seenStages & (1UL << i)) != 0)
            {
                var value = mainStages[slot * MainStageCount + i];
                serializer.Serialize(writer, new { stage = (DiagnosticMainStage)i, inclusiveTicks = value.inclusive,
                    exclusiveTicks = value.exclusive, maxTicks = value.maximum, callCount = mainStageCalls[slot * MainStageCount + i],
                    exclusiveClassification = i == (int)DiagnosticMainStage.WorkloadStep ? "NonDiagnosticResidual" : "MeasuredStage" });
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }

        private void ReleaseMainObservation()
        {
            mainRecords = null; mainStages = null; mainStack = null; mainStageCalls = null;
            DiagnosticMainTiming.Detach(this);
        }
    }
}
