using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MonsterSupergroup.GAS
{
    /// <summary>Transport/Unity independent evidence. Payloads must be detached values, never live actors.</summary>
    public sealed class DiagnosticRecord
    {
        public int schemaVersion = 2;
        public string captureId, runId, role, stage, outcome, reason, engine, operation;
        public string recordSequence, eventId, rootEventId, parentEventId, statusInstanceId, entityGeneration;
        public uint round, source, target, connectionEpoch, assignmentEpoch, batchSequence, serverSequence;
        public uint stateVersion, applicationRevision;
        public int tickIndex, frame, fixedStep;
        public double monotonicTime, networkTime;
        public string utc;
        public object input, before, after;
        public string inputRef, checkpointRef;
        public bool critical;
        public DiagnosticCompletion completion;
        // Conservative retained-memory charge, including payloads. Not a wire-size estimate.
        public int estimatedBytes = 512;
        public DiagnosticRecord Copy() => (DiagnosticRecord)MemberwiseClone();
    }

    [Serializable] public sealed class DiagnosticCompletion
    {
        public string sequence, utc;
        public double monotonic, network;
        public int frame, fixedStep, estimatedBytes;
    }

    public interface IDiagnosticSink
    {
        bool TryWrite(DiagnosticRecord record);
        string RegisterEngine(object engine, string domain, Func<object, object> capture);
    }

    public interface IDiagnosticAdvanceSink
    {
        bool TryWriteAdvance(string role, string engine, string operation, float delta, StatusReplayBoundary boundary, int phase);
    }

    public interface IDiagnosticIntegritySink
    {
        void ReportCaptureFailure(DiagnosticRecord record);
    }

    public static class CombatEvidence
    {
        public static IDiagnosticSink Sink { get; set; }
        [ThreadStatic] private static int suppression;
        [ThreadStatic] private static bool reportingCaptureFailure;
        public static bool Enabled => Sink != null && suppression == 0;
        public static long Failures { get; private set; }

        public static bool Write(DiagnosticRecord record)
        {
            if (!Enabled) return false;
            using var timing = DiagnosticMainTiming.Measure(DiagnosticMainStage.GasWrapper);
            try { return Sink.TryWrite(record); }
            catch (Exception error) { ReportCaptureFailure(record, error); return false; } // Diagnostics must never change a combat outcome.
        }

        public static string Register(object engine, string domain, Func<object, object> capture)
        {
            if (!Enabled) return null;
            using var timing = DiagnosticMainTiming.Measure(DiagnosticMainStage.GasWrapper);
            try { return Sink.RegisterEngine(engine, domain, capture); }
            catch (Exception error)
            {
                ReportCaptureFailure(new DiagnosticRecord { role = domain, stage = "replay.engine_checkpoint", operation = "RegisterEngine" }, error);
                return null;
            }
        }

        public static void ReportCaptureFailure(DiagnosticRecord original, Exception error)
        {
            if (!Enabled || reportingCaptureFailure) return;
            using var timing = DiagnosticMainTiming.Measure(DiagnosticMainStage.GasWrapper);
            reportingCaptureFailure = true;
            try
            {
                Failures++;
                var record = original?.Copy() ?? new DiagnosticRecord();
                record.reason = (record.stage ?? "UnknownStage") + ":" + (error?.GetType().Name ?? "UnknownFailure");
                record.stage = "evidence.gap"; record.outcome = "CaptureFailed"; record.critical = true;
                record.input = null; record.before = null; record.after = null;
                record.inputRef = null; record.checkpointRef = null; record.completion = null; record.estimatedBytes = 512;
                if (Sink is IDiagnosticIntegritySink integrity) integrity.ReportCaptureFailure(record);
                else Sink.TryWrite(record);
            }
            catch { } // Even a failed integrity channel must not escape into gameplay or recurse.
            finally { reportingCaptureFailure = false; }
        }

        public static void ReportCaptureFailure(string role, string stage, Exception error, CombatContext context, string engine = null)
        {
            ReportCaptureFailure(new DiagnosticRecord { role = role, stage = stage, engine = engine,
                eventId = context.EventId.IsValid ? context.EventId.Value.ToString() : null,
                rootEventId = context.RootEventId.IsValid ? context.RootEventId.Value.ToString() : null,
                parentEventId = context.ParentEventId.IsValid ? context.ParentEventId.Value.ToString() : null,
                source = context.SourcePlayerId, target = context.TargetEntityId, stateVersion = context.TargetStateVersion }, error);
        }

        public static IDisposable Suppress() { suppression++; return new Suppression(); }
        private sealed class Suppression : IDisposable
        {
            private bool disposed;
            public void Dispose() { if (!disposed) { disposed = true; suppression--; } }
        }

        private sealed class Binding
        {
            public WeakReference root;
            public string prefix, domain, advanceMethod;
            public Func<object, object> capture;
        }
        private static readonly ConditionalWeakTable<object, Binding> bindings = new();
        [ThreadStatic] private static HashSet<object> operating;
        [ThreadStatic] private static Stack<string> operationIds;
        public static string CurrentEngine => operationIds?.Count > 0 ? operationIds.Peek() : null;
        public static void Bind(object child, object root, string prefix, string domain, Func<object, object> capture)
        {
            bindings.Remove(child);
            bindings.Add(child, new Binding { root = new WeakReference(root), prefix = prefix, domain = domain, capture = capture, advanceMethod = prefix + "Advance" });
        }
        public static bool HasParent(object target) => target != null && bindings.TryGetValue(target, out var binding) && binding.root.IsAlive;
        public static void Unbind(object child) { if (child != null) bindings.Remove(child); }
        public static DiagnosticOperation Begin(object target, string domain, string method, object[] arguments, Func<object, object> capture)
            => BeginCore(target, domain, method, arguments, capture, false, 0);
        public static DiagnosticOperation BeginAdvance(StatusController target, float delta)
            => BeginCore(target, "status", "Advance", null, o => ((StatusController)o).CaptureReplayState(), true, delta);
        private static DiagnosticOperation BeginCore(object target, string domain, string method, object[] arguments, Func<object, object> capture, bool advance, float delta)
        {
            if (!Enabled) return default;
            using var timing = DiagnosticMainTiming.Measure(DiagnosticMainStage.GasWrapper);
            object root = target;
            bool acquired = false, pushed = false;
            string id = null;
            try
            {
                if (bindings.TryGetValue(target, out var binding) && binding.root.Target is object owner)
                { root = owner; method = advance ? binding.advanceMethod : binding.prefix + method; domain = binding.domain; capture = binding.capture; }
                operating ??= new HashSet<object>();
                if (!operating.Add(root)) return default;
                acquired = true;
                id = Register(root, domain, capture);
                if (id == null) { operating.Remove(root); return default; }
                (operationIds ??= new Stack<string>()).Push(id);
                pushed = true;
                StatusReplayBoundary boundary;
                using (DiagnosticMainTiming.Measure(DiagnosticMainStage.ReplayBoundary))
                    boundary = target is StatusController status ? status.CaptureReplayBoundary() : null;
                bool compact = advance && Sink is IDiagnosticAdvanceSink;
                if (compact)
                { try { ((IDiagnosticAdvanceSink)Sink).TryWriteAdvance(domain, id, method, delta, boundary, 0); }
                    catch (Exception error) { ReportCaptureFailure(new DiagnosticRecord { role = domain, stage = "replay.input", engine = id, operation = method }, error); } }
                else Write(new DiagnosticRecord { role = domain, stage = "replay.input", engine = id, operation = method,
                    input = advance ? new object[] { delta } : arguments, before = boundary,
                    estimatedBytes = Estimate(arguments), critical = true });
                return new DiagnosticOperation(root, domain, id, method, compact);
            }
            catch (Exception error)
            {
                if (acquired) operating?.Remove(root);
                if (pushed) operationIds.Pop();
                ReportCaptureFailure(new DiagnosticRecord { role = domain, stage = "replay.input", engine = id, operation = method }, error);
                return default;
            }
        }
        private static int Estimate(object value)
        {
            using var timing = DiagnosticMainTiming.Measure(DiagnosticMainStage.RetainedSize);
            return EstimateCore(value);
        }
        private static int EstimateCore(object value)
        {
            if (value is Array array)
            {
                long size = 64;
                foreach (object item in array) { size += item is Array ? EstimateCore(item) : item is string s ? s.Length * 2L + 64 : 64; if (size > 24 << 20) return 32 << 20; }
                return (int)size + 512;
            }
            return 512;
        }
        public struct DiagnosticOperation : IDisposable
        {
            private object root;
            private readonly string domain, id, method;
            private bool complete;
            private readonly bool compact;
            internal DiagnosticOperation(object root, string domain, string id, string method, bool compact)
            { this.root = root; this.domain = domain; this.id = id; this.method = method; this.compact = compact; complete = false; }
            public void Complete(object result = null)
            {
                if (root == null) return;
                using var timing = DiagnosticMainTiming.Measure(DiagnosticMainStage.GasWrapper);
                complete = true;
                if (compact && Sink is IDiagnosticAdvanceSink sink)
                { try { sink.TryWriteAdvance(domain, id, method, 0, null, 1); }
                    catch (Exception error) { ReportCaptureFailure(new DiagnosticRecord { role = domain, stage = "replay.output", engine = id, operation = method }, error); } return; }
                Write(new DiagnosticRecord { role = domain, stage = "replay.output", engine = id,
                    operation = method, outcome = "Completed", after = result, estimatedBytes = Estimate(result) });
            }
            public void Dispose()
            {
                if (root == null) return;
                using var timing = DiagnosticMainTiming.Measure(DiagnosticMainStage.GasWrapper);
                if (!complete)
                {
                    if (compact && Sink is IDiagnosticAdvanceSink sink)
                    { try { sink.TryWriteAdvance(domain, id, method, 0, null, 2); }
                        catch (Exception error) { ReportCaptureFailure(new DiagnosticRecord { role = domain, stage = "replay.output", engine = id, operation = method }, error); } }
                    else Write(new DiagnosticRecord { role = domain, stage = "replay.output", engine = id,
                        operation = method, outcome = "Aborted", reason = "ExceptionOrEarlyExit", critical = true });
                }
                operating?.Remove(root); operationIds?.Pop(); root = null;
            }
        }

        public static void Event(string role, string stage, string outcome, string reason,
            ulong eventId = 0, uint source = 0, uint target = 0, object input = null,
            object before = null, object after = null, ulong root = 0, ulong parent = 0,
            uint batch = 0, uint server = 0, int bytes = 2048)
        {
            if (!Enabled) return;
            using var timing = DiagnosticMainTiming.Measure(DiagnosticMainStage.GasWrapper);
            Write(new DiagnosticRecord { role = role, engine = CurrentEngine, stage = stage, outcome = outcome, reason = reason,
                eventId = eventId == 0 ? null : eventId.ToString(), rootEventId = root == 0 ? null : root.ToString(),
                parentEventId = parent == 0 ? null : parent.ToString(), source = source, target = target,
                input = input, before = before, after = after, batchSequence = batch, serverSequence = server,
                estimatedBytes = bytes, critical = true });
        }
    }
}
