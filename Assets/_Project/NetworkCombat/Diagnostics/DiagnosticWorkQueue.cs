using System;
using System.Collections.Generic;
using System.Threading;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    internal sealed class DiagnosticWorkQueue : IDisposable
    {
        private readonly object gate = new();
        private readonly Queue<(int bytes, Action work)> queue = new();
        private readonly AutoResetEvent wake = new(false);
        private readonly DiagnosticMemoryBudget memory;
        private readonly Action<string> failure;
        private readonly Thread worker;
        private readonly int workspace;
        private long pending;
        private bool stopping;
        public DiagnosticWorkQueue(DiagnosticMemoryBudget memory, Action<string> failure, int workspace = 16 << 20)
        {
            this.memory = memory; this.failure = failure; this.workspace = workspace;
            if (!memory.TryReserve(workspace)) throw new InvalidOperationException("DiagnosticReplicationBudgetUnavailable");
            worker = new Thread(Run) { IsBackground = true, Name = "Combat evidence replication" }; worker.Start();
        }
        public bool TrySchedule(int bytes, Action action) => TryCapture(bytes, () => action);
        public bool TryCapture(int bytes, Func<Action> capture)
        {
            lock (gate)
            {
                if (stopping || bytes < 0 || pending + bytes > (8 << 20) || !memory.TryReserve(bytes)) return false;
                try { var action = capture(); pending += bytes; queue.Enqueue((bytes, action)); }
                catch (Exception error) { memory.Release(bytes); failure(error.GetType().Name + ": " + error.Message); return false; }
            }
            wake.Set(); return true;
        }
        private void Run()
        {
            try
            {
                while (true)
                {
                    (int bytes, Action work) item = default;
                    lock (gate) { if (queue.Count != 0) item = queue.Dequeue(); else if (stopping) break; }
                    if (item.work == null) { wake.WaitOne(50); continue; }
                    try { item.work(); } catch (Exception error) { failure(error.GetType().Name + ": " + error.Message); }
                    finally { lock (gate) pending -= item.bytes; memory.Release(item.bytes); }
                }
            }
            finally { memory.Release(workspace); }
        }
        public void Dispose() { lock (gate) stopping = true; wake.Set(); worker.Join(100); }
        public bool WaitForClose(int milliseconds = 5000) => worker.Join(milliseconds);
    }
}
