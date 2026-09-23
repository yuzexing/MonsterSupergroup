using System;
using System.Threading;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    public sealed partial class CombatEvidenceStore
    {
        private void CountDropped(EvidenceCoverage state, bool observation = false)
        {
            state.dropped++; Interlocked.Increment(ref dropped);
            if (observation) state.observationDropped++; else state.criticalDropped++;
        }
        private static void EnsureIntegrityHistory(EvidenceCoverage state)
        {
            bool oldEvidence = state.failureEpoch != 0 || state.recoveryPending || state.failure != null ||
                state.failureFirstSequence != null || state.failureLastSequence != null || state.reliableFromSequence != null || state.gaps.Count != 0;
            if (state.integrityHistoryVersion == 1 && state.integrityHistory != null && (state.integrityHistory.Count != 0 || !oldEvidence)) return;
            state.integrityHistory ??= new System.Collections.Generic.List<EvidenceIntegrityEpisode>();
            state.integrityHistory.Clear();
            if (oldEvidence)
            {
                // Legacy summaries cannot recreate earlier certified windows. Preserve their uncertainty explicitly.
                state.integrityHistory.Add(new EvidenceIntegrityEpisode {
                    First = ulong.TryParse(state.failureFirstSequence, out var first) ? first : (ulong?)null,
                    Last = ulong.TryParse(state.failureLastSequence, out var last) ? last : (ulong?)null,
                    epoch = state.failureEpoch, reliableFromSequence = state.recoveryPending ? null : state.reliableFromSequence,
                    conservative = true });
            }
            state.integrityHistoryVersion = 1;
        }
        private static void CoalesceOldestIntegrityEpisodes(EvidenceCoverage state)
        {
            var first = state.integrityHistory[0]; var second = state.integrityHistory[1];
            first.First = first.First.HasValue && second.First.HasValue ? Math.Min(first.First.Value, second.First.Value) : (ulong?)null;
            first.Last = first.Last.HasValue && second.Last.HasValue ? Math.Max(first.Last.Value, second.Last.Value) : (ulong?)null;
            first.epoch = second.epoch; first.reliableFromSequence = second.reliableFromSequence; first.conservative = true;
            first.tailMarker = first.Last.HasValue && second.tailMarker;
            state.integrityHistory.RemoveAt(1);
        }
        private static void MarkRecoveryPending(EvidenceCoverage state, ulong? first = null, ulong? last = null, bool tailMarker = false)
        {
            EnsureIntegrityHistory(state);
            state.failureEpoch++;
            var history = state.integrityHistory;
            var episode = history.Count == 0 ? null : history[history.Count - 1];
            if (episode == null || episode.reliableFromSequence != null)
            {
                if (history.Count == 128) CoalesceOldestIntegrityEpisodes(state);
                episode = new EvidenceIntegrityEpisode { First = first, Last = last, conservative = !first.HasValue || !last.HasValue, tailMarker = tailMarker };
                history.Add(episode);
            }
            else if (!tailMarker)
            {
                episode.First = episode.First.HasValue && first.HasValue ? Math.Min(episode.First.Value, first.Value) : (ulong?)null;
                episode.Last = episode.Last.HasValue && last.HasValue ? Math.Max(episode.Last.Value, last.Value) : (ulong?)null;
                episode.conservative |= !first.HasValue || !last.HasValue;
                episode.tailMarker = false;
            }
            // An empty flush failure adds no lost record to an already pending episode; its new epoch still invalidates queued checkpoints.
            episode.epoch = state.failureEpoch;
            state.recoveryPending = true;
            state.recoveryCheckpoint = null;
            state.complete = false;
        }

        private static void MarkFailureRange(EvidenceCoverage state, ulong first, ulong last)
            => MarkFailureRangeCore(state, first, last, false);
        private static void MarkTailFailure(EvidenceCoverage state, ulong first)
            => MarkFailureRangeCore(state, first, first, true);
        private static void MarkFailureRangeCore(EvidenceCoverage state, ulong first, ulong last, bool tailMarker)
        {
            MarkRecoveryPending(state, first, last, tailMarker);
            state.failureFirstSequence = ulong.TryParse(state.failureFirstSequence, out var priorFirst) ? Math.Min(first, priorFirst).ToString() : first.ToString();
            state.failureLastSequence = ulong.TryParse(state.failureLastSequence, out var priorLast) ? Math.Max(last, priorLast).ToString() : last.ToString();
        }

        private static void PublishRecovery(EvidenceCoverage state)
        {
            if (state.recoveryCheckpoint == null || state.recoveryCheckpointEpoch != state.failureEpoch ||
                ulong.Parse(state.flushed) < ulong.Parse(state.recoveryCheckpoint)) return;
            EnsureIntegrityHistory(state);
            var history = state.integrityHistory;
            if (history.Count == 0 || history[history.Count - 1].epoch != state.failureEpoch) return;
            history[history.Count - 1].reliableFromSequence = state.recoveryCheckpoint;
            state.reliableFromSequence = state.recoveryCheckpoint;
            state.recoveryPending = false; state.recoveryCheckpoint = null;
        }

        // This control path does not allocate or enqueue an ordinary record. A saturated queue
        // must never prevent a capture failure from appearing in the next durable coverage.
        public void ReportCaptureFailure(DiagnosticRecord record)
        {
            string key = SourceKey(record.runId, record.round, record.captureId);
            lock (gate)
            {
                if (!health.TryGetValue(key, out var state)) health.Add(key, state = new EvidenceCoverage {
                    captureId = record.captureId, runId = record.runId, round = record.round });
                if (ulong.Parse(record.recordSequence) > ulong.Parse(state.produced)) state.produced = record.recordSequence;
                CountDropped(state);
                ulong sequence = ulong.Parse(record.recordSequence);
                MarkFailureRange(state, sequence, sequence);
                state.failure = "CaptureFailure:" + record.reason;
                Gap(state, record.recordSequence, state.failure);
                var gap = state.gaps.Find(g => g.First <= sequence && sequence <= g.Last);
                if (gap != null)
                {
                    string stage = record.operation ?? record.reason;
                    if (gap.count == 1) { gap.engine = record.engine; gap.eventId = record.eventId; gap.stage = stage; }
                    else
                    {
                        if (gap.engine != record.engine) { gap.engine = null; gap.conservative = true; }
                        if (gap.eventId != record.eventId) gap.eventId = null;
                        if (gap.stage != stage) gap.stage = null;
                    }
                }
            }
            wake.Set();
        }

        public bool RecoveryCheckpointRequested(string capture, string run, uint round)
        {
            string key = SourceKey(run, round, capture);
            lock (gate) return health.TryGetValue(key, out var state) && state.recoveryPending && state.recoveryCheckpoint == null;
        }
    }
}
