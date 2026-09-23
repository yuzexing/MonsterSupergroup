using System.Diagnostics;
using System.Threading;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    // Cumulative counters only; measuring evidence must never generate more evidence records.
    public sealed class EvidenceStageMetrics
    {
        private long lockWait, queueWait, freeze, capture, encoding, compression, storage, queued;
        private long binaryBuild, blockHash, blockHeader;
        internal static long Now => Stopwatch.GetTimestamp();
        internal long CompressionTicks => Interlocked.Read(ref compression);
        internal void LockWait(long start) => Interlocked.Add(ref lockWait, Now - start);
        internal void QueueWait(long start) { Interlocked.Add(ref queueWait, Now - start); Interlocked.Increment(ref queued); }
        internal void Freeze(long start) => Interlocked.Add(ref freeze, Now - start);
        internal void Capture(long start) => Interlocked.Add(ref capture, Now - start);
        internal void Encoding(long start, long compressionBefore = -1) => Interlocked.Add(ref encoding,
            Now - start - (compressionBefore < 0 ? 0 : CompressionTicks - compressionBefore));
        internal void Compression(long start) => Interlocked.Add(ref compression, Now - start);
        internal void BinaryBuild(long start) => Interlocked.Add(ref binaryBuild, Now - start);
        internal void BlockHash(long start) => Interlocked.Add(ref blockHash, Now - start);
        internal void BlockHeader(long start) => Interlocked.Add(ref blockHeader, Now - start);
        internal void Storage(long start) => Interlocked.Add(ref storage, Now - start);
        private static double Milliseconds(ref long value) => Interlocked.Read(ref value) * 1000d / Stopwatch.Frequency;
        public object Snapshot() => new { lockWaitMs = Milliseconds(ref lockWait), queueWaitMs = Milliseconds(ref queueWait),
            freezeMs = Milliseconds(ref freeze), captureMs = Milliseconds(ref capture), encodingMs = Milliseconds(ref encoding),
            compressionMs = Milliseconds(ref compression), storageAndFlushMs = Milliseconds(ref storage), queueWorkItems = Interlocked.Read(ref queued),
            binaryBuildMs = Milliseconds(ref binaryBuild), blockHashMs = Milliseconds(ref blockHash), blockHeaderMs = Milliseconds(ref blockHeader) };
    }
}
