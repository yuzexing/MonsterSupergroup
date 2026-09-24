using System;
using System.Diagnostics;

namespace MonsterSupergroup.GAS
{
    public enum DiagnosticMainStage
    {
        WorkloadStep, GasWrapper, ReplayBoundary, SinkMetadata, RetainedSize, ProducerGateWait,
        ProducerGateWork, Capture, Freeze, AdvanceBlock, Wake, ReplicationTick, Checkpoint,
        SharedLeases, Admission, Registry
    }

    public interface IDiagnosticMainTiming
    {
        void BeginMainStage(DiagnosticMainStage stage, long ticks);
        void EndMainStage(DiagnosticMainStage stage, long ticks);
    }

    // GAS owns the interface. No Unity/NetworkCombat reference or allocated per-event token is needed.
    public static class DiagnosticMainTiming
    {
        [ThreadStatic] private static IDiagnosticMainTiming current;
        [ThreadStatic] private static bool faulted;

        public static bool TryAttach(IDiagnosticMainTiming observer)
        {
            if (observer == null || current != null) return false;
            current = observer; faulted = false; return true;
        }

        public static bool HasFault(IDiagnosticMainTiming observer) => ReferenceEquals(current, observer) && faulted;

        public static void Detach(IDiagnosticMainTiming observer)
        {
            if (!ReferenceEquals(current, observer)) return;
            current = null; faulted = false;
        }

        public static DiagnosticMainScope Measure(DiagnosticMainStage stage)
        {
            var observer = current;
            if (observer == null) return default; // No timestamp read on the disabled path.
            try
            {
                observer.BeginMainStage(stage, Stopwatch.GetTimestamp());
                return new DiagnosticMainScope(observer, stage);
            }
            catch { faulted = true; return default; }
        }

        internal static void End(IDiagnosticMainTiming observer, DiagnosticMainStage stage)
        {
            if (observer == null) return;
            try { observer.EndMainStage(stage, Stopwatch.GetTimestamp()); }
            catch { if (ReferenceEquals(current, observer)) faulted = true; }
        }
    }

    public readonly struct DiagnosticMainScope : IDisposable
    {
        private readonly IDiagnosticMainTiming observer;
        private readonly DiagnosticMainStage stage;
        internal DiagnosticMainScope(IDiagnosticMainTiming observer, DiagnosticMainStage stage)
        { this.observer = observer; this.stage = stage; }
        public void Dispose() => DiagnosticMainTiming.End(observer, stage);
    }
}
