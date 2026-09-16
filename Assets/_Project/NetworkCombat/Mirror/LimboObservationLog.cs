using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>One bounded background writer for the existing observers. Only explicit
    /// end/exit Flush and Dispose wait; periodic flushing never blocks gameplay.</summary>
    public sealed class LimboObservationLog : IDisposable
    {
        public const int MaximumPendingBytes = 4 * 1024 * 1024;
        private static readonly HashSet<LimboObservationLog> Open = new();
        private static readonly object Gate = new();
        private static readonly Queue<Work> Queue = new();
        private static readonly AutoResetEvent Wake = new(false);
        private static Thread worker;
        private static long pendingBytes, writtenTicks, flushTicks, lines;
        private static int failures;
        private enum Operation { Line, Flush, Close, Status }
        private readonly struct Work
        {
            public readonly LimboObservationLog Log;
            public readonly Operation Op;
            public readonly string Text;
            public readonly int Bytes;
            public readonly ManualResetEventSlim Completion;
            public Work(LimboObservationLog log, Operation op, string text = null, ManualResetEventSlim completion = null)
            { Log = log; Op = op; Text = text; Bytes = text == null ? 0 : checked(text.Length * 2 + 64); Completion = completion; }
        }

        private readonly StreamWriter writer;
        private readonly Func<double> clock;
        private readonly string statusPath;
        private double nextFlush;
        private bool disposed, flushQueued;
        private string failure;
        public string Failure { get { lock (Gate) return failure; } }
        public bool IsComplete => disposed && Failure == null;
        public static double WriteMilliseconds => Interlocked.Read(ref writtenTicks) * 1000d / Stopwatch.Frequency;
        public static double FlushMilliseconds => Interlocked.Read(ref flushTicks) * 1000d / Stopwatch.Frequency;
        public static long Lines => Interlocked.Read(ref lines);
        public static long PendingBytes => Interlocked.Read(ref pendingBytes);
        public static int FailureCount => Volatile.Read(ref failures);
        public static int OpenCount => Open.Count;

        public LimboObservationLog(string path) : this(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
            () => Time.realtimeSinceStartupAsDouble, path + ".status.json") { }
        public LimboObservationLog(Stream stream, Func<double> clock) : this(stream, clock, null) { }
        private LimboObservationLog(Stream stream, Func<double> clock, string statusPath)
        {
            this.clock = clock; this.statusPath = statusPath;
            writer = new StreamWriter(stream, new UTF8Encoding(false), 16 * 1024) { AutoFlush = false };
            nextFlush = clock() + 1;
            Open.Add(this);
            lock (Gate)
            {
                if (worker == null)
                {
                    worker = new Thread(Consume) { IsBackground = true, Name = "Limbo observation files" };
                    worker.Start();
                }
                Queue.Enqueue(new Work(this, Operation.Status));
            }
            Wake.Set();
        }

        public void WriteLine(string value)
        {
            if (disposed) throw new ObjectDisposedException(nameof(LimboObservationLog));
            var work = new Work(this, Operation.Line, value ?? string.Empty);
            lock (Gate)
            {
                if (failure != null) return;
                if (pendingBytes + work.Bytes > MaximumPendingBytes)
                { FailLocked("Observation queue exceeded 4 MiB; evidence is incomplete."); return; }
                Interlocked.Add(ref pendingBytes, work.Bytes);
                Queue.Enqueue(work);
                Interlocked.Increment(ref lines);
            }
            Wake.Set();
            FlushIfDue();
        }

        public void FlushIfDue()
        {
            if (disposed || clock() < nextFlush) return;
            nextFlush = clock() + 1;
            lock (Gate)
            {
                if (flushQueued) return;
                flushQueued = true;
                Queue.Enqueue(new Work(this, Operation.Flush));
            }
            Wake.Set();
        }
        // Explicit lifecycle boundaries/tests only. Per-frame pumping uses FlushIfDue.
        public void Flush() { if (!disposed) WaitFor(Operation.Flush); }
        private void WaitFor(Operation operation)
        {
            var completion = new ManualResetEventSlim();
            lock (Gate) Queue.Enqueue(new Work(this, operation, completion: completion));
            Wake.Set();
            if (!completion.Wait(TimeSpan.FromSeconds(5)))
                lock (Gate) FailLocked("Timed out draining observation files; evidence is incomplete.");
            // Worker still owns the completion and stream after a timeout.
        }
        public static void FlushDue() { foreach (var log in Open) log.FlushIfDue(); }
        public static void FlushAll() { foreach (var log in Open) log.Flush(); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            WaitFor(Operation.Close);
            Open.Remove(this);
        }

        private void FailLocked(string reason)
        {
            if (failure != null) return;
            failure = reason; Interlocked.Increment(ref failures);
            Queue.Enqueue(new Work(this, Operation.Status));
            Wake.Set();
        }

        private static void Consume()
        {
            while (true)
            {
                Work work;
                lock (Gate) work = Queue.Count == 0 ? default : Queue.Dequeue();
                if (work.Log == null) { Wake.WaitOne(); continue; }
                var log = work.Log;
                try
                {
                    long start = Stopwatch.GetTimestamp();
                    switch (work.Op)
                    {
                        case Operation.Line:
                            log.writer.WriteLine(work.Text);
                            Interlocked.Add(ref writtenTicks, Stopwatch.GetTimestamp() - start);
                            break;
                        case Operation.Flush:
                            log.writer.Flush();
                            Interlocked.Add(ref flushTicks, Stopwatch.GetTimestamp() - start);
                            lock (Gate) log.flushQueued = false;
                            break;
                        case Operation.Close:
                            log.writer.Dispose();
                            Interlocked.Add(ref flushTicks, Stopwatch.GetTimestamp() - start);
                            log.WriteStatus(true);
                            break;
                        case Operation.Status: log.WriteStatus(false); break;
                    }
                }
                catch (Exception e)
                {
                    // No Unity calls on this thread, including Debug.Log.
                    lock (Gate) log.FailLocked(e.GetType().Name + ": " + e.Message);
                }
                finally
                {
                    Interlocked.Add(ref pendingBytes, -work.Bytes);
                    work.Completion?.Set();
                }
            }
        }

        private void WriteStatus(bool closed)
        {
            if (statusPath == null) return;
            string reason = Failure;
            string escaped = (reason ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
            File.WriteAllText(statusPath, "{\"complete\":" + (closed && reason == null ? "true" : "false") + ",\"failure\":\"" + escaped + "\"}", new UTF8Encoding(false));
        }
    }
}
