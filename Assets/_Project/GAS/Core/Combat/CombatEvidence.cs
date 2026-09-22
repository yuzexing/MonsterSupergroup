using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MonsterSupergroup.GAS
{
    /// <summary>Transport/Unity independent evidence. Payloads must be detached values, never live actors.</summary>
    public sealed class DiagnosticRecord
    {
        public int schemaVersion = 1;
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
        // Conservative retained-memory charge, including payloads. Not a wire-size estimate.
        public int estimatedBytes = 2048;
    }

    public interface IDiagnosticSink
    {
        bool TryWrite(DiagnosticRecord record);
        string RegisterEngine(object engine, string domain, Func<object, object> capture);
    }

    public static class CombatEvidence
    {
        public static IDiagnosticSink Sink { get; set; }
        [ThreadStatic] private static int suppression;
        public static bool Enabled => Sink != null && suppression == 0;
        public static long Failures { get; private set; }

        public static bool Write(DiagnosticRecord record)
        {
            if (!Enabled) return false;
            try { return Sink.TryWrite(record); }
            catch { Failures++; return false; } // Diagnostics must never change a combat outcome.
        }

        public static string Register(object engine, string domain, Func<object, object> capture)
        {
            if (!Enabled) return null;
            try { return Sink.RegisterEngine(engine, domain, capture); }
            catch { Failures++; return null; }
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
            public string prefix, domain;
            public Func<object, object> capture;
        }
        private static readonly ConditionalWeakTable<object, Binding> bindings = new();
        [ThreadStatic] private static HashSet<object> operating;
        [ThreadStatic] private static Stack<string> operationIds;
        public static string CurrentEngine => operationIds?.Count > 0 ? operationIds.Peek() : null;
        public static void Bind(object child, object root, string prefix, string domain, Func<object, object> capture)
        {
            bindings.Remove(child);
            bindings.Add(child, new Binding { root = new WeakReference(root), prefix = prefix, domain = domain, capture = capture });
        }
        public static bool HasParent(object target) => target != null && bindings.TryGetValue(target, out var binding) && binding.root.IsAlive;
        public static void Unbind(object child) { if (child != null) bindings.Remove(child); }
        public static DiagnosticOperation Begin(object target, string domain, string method, object[] arguments, Func<object, object> capture)
        {
            if (!Enabled) return default;
            object root = target;
            bool acquired = false;
            try
            {
                if (bindings.TryGetValue(target, out var binding) && binding.root.Target is object owner)
                { root = owner; method = binding.prefix + method; domain = binding.domain; capture = binding.capture; }
                operating ??= new HashSet<object>();
                if (!operating.Add(root)) return default;
                acquired = true;
                string id = Register(root, domain, capture);
                if (id == null) { operating.Remove(root); return default; }
                (operationIds ??= new Stack<string>()).Push(id);
                Write(new DiagnosticRecord { role = domain, stage = "replay.input", engine = id, operation = method,
                    input = arguments, before = target is StatusController status ? status.CaptureReplayBoundary() : null,
                    estimatedBytes = Estimate(arguments), critical = true });
                return new DiagnosticOperation(root, domain, id, method);
            }
            catch { if (acquired) operating?.Remove(root); Failures++; return default; }
        }
        private static int Estimate(object value)
        {
            if (value is Array array)
            {
                long size = 256;
                foreach (object item in array) { size += item is Array ? Estimate(item) : item is string s ? s.Length * 2L + 128 : 1024; if (size > 24 << 20) return 32 << 20; }
                return (int)size + 2048;
            }
            return 2048;
        }
        public struct DiagnosticOperation : IDisposable
        {
            private object root;
            private readonly string domain, id, method;
            private bool complete;
            internal DiagnosticOperation(object root, string domain, string id, string method)
            { this.root = root; this.domain = domain; this.id = id; this.method = method; complete = false; }
            public void Complete(object result = null)
            {
                if (root == null) return;
                complete = true;
                Write(new DiagnosticRecord { role = domain, stage = "replay.output", engine = id,
                    operation = method, outcome = "Completed", after = result, estimatedBytes = Estimate(result) });
            }
            public void Dispose()
            {
                if (root == null) return;
                if (!complete) Write(new DiagnosticRecord { role = domain, stage = "replay.output", engine = id,
                    operation = method, outcome = "Aborted", reason = "ExceptionOrEarlyExit", critical = true });
                operating?.Remove(root); operationIds?.Pop(); root = null;
            }
        }

        public static void Event(string role, string stage, string outcome, string reason,
            ulong eventId = 0, uint source = 0, uint target = 0, object input = null,
            object before = null, object after = null, ulong root = 0, ulong parent = 0,
            uint batch = 0, uint server = 0, int bytes = 2048)
        {
            if (!Enabled) return;
            Write(new DiagnosticRecord { role = role, engine = CurrentEngine, stage = stage, outcome = outcome, reason = reason,
                eventId = eventId == 0 ? null : eventId.ToString(), rootEventId = root == 0 ? null : root.ToString(),
                parentEventId = parent == 0 ? null : parent.ToString(), source = source, target = target,
                input = input, before = before, after = after, batchSequence = batch, serverSequence = server,
                estimatedBytes = bytes });
        }
    }
}
