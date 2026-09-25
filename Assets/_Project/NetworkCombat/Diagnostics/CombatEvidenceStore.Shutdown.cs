using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    public readonly struct EvidenceShutdownProgress
    {
        public readonly long pendingBytes, writeTicks;
        public readonly bool writerJoined;
        public EvidenceShutdownProgress(long pending, long ticks, bool joined)
        { pendingBytes = pending; writeTicks = ticks; writerJoined = joined; }
    }

    [Serializable]
    public sealed class EvidenceShutdownAssessment
    {
        public bool complete;
        public int sources;
        public long pendingBytes, dropped;
        public bool? countsBalanced;
        public string[] failures;
    }

    public sealed partial class CombatEvidenceStore
    {
        private string shutdownWorkerFailure;
        private string shutdownLocalRetention;

        // Never take the producer gate here: observation export can own it while doing slow I/O.
        public EvidenceShutdownProgress ReadShutdownProgress() => new(
            Interlocked.Read(ref pendingBytes), Interlocked.Read(ref writeTicks), worker.Join(0));

        private void RecordShutdownWorkerFailure(Exception error) =>
            Interlocked.CompareExchange(ref shutdownWorkerFailure, error.GetType().Name + ":" + error.Message, null);

        // This marker outlives health entries removed by global retention. Historical/imported
        // runs are not this store's local production and must not invalidate the current capture.
        private void RecordLocalRetention(string reason) =>
            Interlocked.CompareExchange(ref shutdownLocalRetention, reason, null);

        // Called by the finalization worker only after the disk worker has stopped. A stopped worker
        // is not proof of complete evidence: verify every original context and its published coverage.
        public EvidenceShutdownAssessment AssessShutdownCompleteness()
        {
            var failures = new List<string>();
            var progress = ReadShutdownProgress();
            var result = new EvidenceShutdownAssessment { pendingBytes = progress.pendingBytes, dropped = Dropped };
            if (!progress.writerJoined) failures.Add("WriterNotJoined");
            if (progress.pendingBytes != 0) failures.Add("PendingRecords");
            if (result.dropped != 0) failures.Add("DroppedRecords");
            if (shutdownWorkerFailure != null) failures.Add("WorkerFailure:" + shutdownWorkerFailure);
            if (shutdownLocalRetention != null) failures.Add("LocalCaptureRetention:" + shutdownLocalRetention);
            if (progress.writerJoined)
            {
                if (ObservationRequested)
                {
                    if (QueueObservation == null) failures.Add("QueueObservationUnavailable:" + ObservationUnavailableReason);
                    else
                    {
                        result.countsBalanced = QueueObservation.CountsBalancedAfterStop(true, true);
                        if (!result.countsBalanced.Value) failures.Add("UnbalancedWriterCounts");
                    }
                }
                KeyValuePair<string, EvidenceCoverage>[] contexts;
                lock (gate) contexts = health.ToArray();
                result.sources = contexts.Length;
                if (contexts.Length == 0) failures.Add("NoCaptureContexts");
                foreach (var context in contexts)
                {
                    var expected = context.Value;
                    if (!CoverageComplete(expected)) failures.Add(context.Key + ":IncompleteCoverage");
                    try
                    {
                        var persisted = EvidenceJson.Decode<EvidenceCoverage>(File.ReadAllText(Path.Combine(Resolve(context.Key), "coverage.json")));
                        if (persisted == null || !CoverageComplete(persisted) ||
                            persisted.captureId != expected.captureId || persisted.runId != expected.runId || persisted.round != expected.round ||
                            persisted.produced != expected.produced || persisted.written != expected.written || persisted.flushed != expected.flushed ||
                            persisted.coverageRevision != expected.coverageRevision || persisted.failureEpoch != expected.failureEpoch)
                            failures.Add(context.Key + ":PersistedCoverageMismatch");
                    }
                    catch (Exception error) { failures.Add(context.Key + ":CoverageReadFailed:" + error.GetType().Name); }
                }
            }
            result.failures = failures.ToArray(); result.complete = failures.Count == 0;
            return result;
        }

        private static bool CoverageComplete(EvidenceCoverage value) => value.complete && !value.tailUnknown &&
            value.dropped == 0 && value.failure == null && !value.recoveryPending &&
            value.gaps != null && value.gaps.Count == 0 && value.integrityHistory != null && value.integrityHistory.Count == 0 &&
            value.produced == value.written && value.written == value.flushed;
    }
}
