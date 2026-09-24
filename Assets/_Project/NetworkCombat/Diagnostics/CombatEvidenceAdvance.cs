using System;
using System.Threading;
using MonsterSupergroup.GAS;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    public struct DiagnosticAdvance
    {
        public string role, engine, operation;
        public ulong sequence;
        public long utcTicks;
        public double monotonic, network;
        public int frame, fixedStep, phase;
        public float delta;
        public StatusReplayBoundary boundary;
        public DiagnosticRecord Expand(string capture, string run, uint round) => new DiagnosticRecord {
            captureId = capture, runId = run, round = round, role = role, engine = engine, operation = operation,
            recordSequence = sequence.ToString(), utc = new DateTime(utcTicks, DateTimeKind.Utc).ToString("o"),
            monotonicTime = monotonic, networkTime = network, frame = frame, fixedStep = fixedStep,
            stage = phase == 0 ? "replay.input" : "replay.output", outcome = phase == 0 ? null : phase == 1 ? "Completed" : "Aborted",
            reason = phase == 2 ? "ExceptionOrEarlyExit" : null, input = phase == 0 ? new object[] { delta } : null,
            before = boundary, critical = phase != 1, estimatedBytes = 512 };
    }

    public sealed partial class CombatEvidenceRuntime
    {
        public bool TryWriteAdvance(string role, string engine, string operation, float delta, StatusReplayBoundary boundary, int phase)
        {
            if (store == null || shuttingDown) return false;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                return store.TryWriteAdvance(capture, run, round, new DiagnosticAdvance {
                    role = role, engine = engine, operation = operation, delta = delta, boundary = boundary, phase = phase,
                    sequence = (ulong)Interlocked.Increment(ref sequence), utcTicks = DateTime.UtcNow.Ticks,
                    monotonic = started / (double)System.Diagnostics.Stopwatch.Frequency, network = NetworkTime.time,
                    frame = Time.frameCount, fixedStep = fixedStep });
            }
            finally { Interlocked.Add(ref sinkTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started); Interlocked.Increment(ref sinkCalls); }
        }
    }

    public sealed partial class CombatEvidenceStore
    {
        // Each block reserves its array, detached boundaries and closure before allocation. The writer seals it under gate.
        private sealed class AdvanceBlock
        {
            public const int Capacity = 128, Charge = 64 << 10;
            public readonly DiagnosticAdvance[] entries = new DiagnosticAdvance[Capacity];
            public string key, capture, run;
            public uint round;
            public int count;
            public readonly double created = Seconds;
        }
        private AdvanceBlock activeAdvances;
        private void BindAdvanceService(AdvanceBlock block)
        {
            if (QueueObservation == null || block.count == 0) return;
            var first = block.entries[0]; var last = block.entries[block.count - 1];
            string engine = first.engine, operation = first.operation;
            bool mixedPhases = false;
            for (int i = 1; i < block.count; i++)
            {
                var entry = block.entries[i];
                mixedPhases |= entry.phase != first.phase;
                if (entry.engine != engine) engine = null;
                if (entry.operation != operation) operation = null;
            }
            QueueObservation.BindServiceIdentity(new EvidenceServiceIdentity {
                kind = EvidenceServiceWorkKind.Advance, runId = block.run, captureId = block.capture, round = block.round,
                firstSequence = first.sequence, lastSequence = last.sequence, logicalCount = block.count,
                engine = engine, operation = operation, stage = "replay.advance_block", phase = mixedPhases ? -1 : first.phase, mixedPhases = mixedPhases });
        }
        public bool TryWriteAdvance(string capture, string run, uint round, DiagnosticAdvance entry)
        {
            bool signal = false;
            long lockStarted = EvidenceStageMetrics.Now;
            var producerWaiting = DiagnosticMainTiming.Measure(DiagnosticMainStage.ProducerGateWait);
            bool waitEnded = false;
            try
            {
                lock (gate)
                {
                    producerWaiting.Dispose(); waitEnded = true;
                    using var gateWork = DiagnosticMainTiming.Measure(DiagnosticMainStage.ProducerGateWork);
                    Metrics.LockWait(lockStarted);
                    QueueObservation?.Attempt(EvidenceQueueEntry.TryWriteAdvance, EvidenceStageMetrics.Now);
                    string key;
                    using (DiagnosticMainTiming.Measure(DiagnosticMainStage.SinkMetadata))
                        key = activeAdvances != null && activeAdvances.capture == capture && activeAdvances.run == run && activeAdvances.round == round
                            ? activeAdvances.key : SourceKey(run, round, capture);
                    if (!health.TryGetValue(key, out var state)) health.Add(key, state = new EvidenceCoverage { captureId = capture, runId = run, round = round });
                    state.Produced = entry.sequence;
                    if (activeAdvances == null || activeAdvances.key != key || activeAdvances.count == AdvanceBlock.Capacity)
                    {
                        if (!Admit(EvidenceQueueEntry.TryWriteAdvance, AdvanceBlock.Charge, options.QueueBytes - options.ReservedBytes, false,
                            state, entry.sequence, entry.phase, entry.phase == 0 ? "replay.input" : "replay.output"))
                        { CountDropped(state); Gap(state, state.produced, "QueueOverload"); return false; }
                        using var constructing = DiagnosticMainTiming.Measure(DiagnosticMainStage.AdvanceBlock);
                        var block = new AdvanceBlock { key = key, capture = capture, run = run, round = round };
                        activeAdvances = block; pendingBytes += AdvanceBlock.Charge; peakPendingBytes = Math.Max(peakPendingBytes, pendingBytes);
                        queue.Enqueue((AdvanceBlock.Charge, () => {
                            long waitStarted = QueueObservation == null ? 0 : EvidenceStageMetrics.Now;
                            var waiting = EvidenceServiceTiming.Measure(EvidenceServiceStage.FilesGateWait);
                            lock (filesGate)
                            {
                                waiting.Dispose();
                                QueueObservation?.Span(EvidenceQueueStage.FilesGateWait, waitStarted, EvidenceStageMetrics.Now);
                                var record = block.entries[0].Expand(block.capture, block.run, block.round);
                                record.stage = "replay.advance_block"; record.input = block; record.before = null;
                                Append(block.key, record);
                            }
                        }, block, EvidenceStageMetrics.Now));
                        QueueObservation?.Enqueued(AdvanceBlock.Charge, pendingBytes, EvidenceStageMetrics.Now);
                        signal = true;
                    }
                    using (DiagnosticMainTiming.Measure(DiagnosticMainStage.AdvanceBlock))
                    {
                        activeAdvances.entries[activeAdvances.count++] = entry;
                        QueueObservation?.Accepted(EvidenceQueueEntry.TryWriteAdvance, EvidenceStageMetrics.Now);
                        QueueObservation?.Pending(pendingBytes, EvidenceStageMetrics.Now);
                        signal |= activeAdvances.count == AdvanceBlock.Capacity;
                    }
                }
            }
            finally { if (!waitEnded) producerWaiting.Dispose(); }
            if (signal) { using (DiagnosticMainTiming.Measure(DiagnosticMainStage.Wake)) wake.Set(); }
            return true;
        }
    }
}
