using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    // Bounded synchronous buffering for the existing reference observers; no event queue.
    public sealed class LimboObservationLog : IDisposable
    {
        private static readonly HashSet<LimboObservationLog> Open = new();
        private readonly StreamWriter writer;
        private readonly Func<double> clock;
        private double nextFlush;
        private bool disposed;
        public static double WriteMilliseconds { get; private set; }
        public static double FlushMilliseconds { get; private set; }
        public static long Lines { get; private set; }
        public static int OpenCount => Open.Count;

        public LimboObservationLog(string path) : this(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
            () => Time.realtimeSinceStartupAsDouble) { }

        public LimboObservationLog(Stream stream, Func<double> clock)
        {
            this.clock = clock;
            writer = new StreamWriter(stream, new UTF8Encoding(false), 16 * 1024) { AutoFlush = false };
            nextFlush = clock() + 1;
            Open.Add(this);
        }

        public void WriteLine(string value)
        {
            if (disposed) throw new ObjectDisposedException(nameof(LimboObservationLog));
            long start = Stopwatch.GetTimestamp();
            writer.WriteLine(value); Lines++;
            WriteMilliseconds += (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            FlushIfDue();
        }

        public void FlushIfDue() { if (!disposed && clock() >= nextFlush) Flush(); }
        public void Flush()
        {
            if (disposed) return;
            long start = Stopwatch.GetTimestamp();
            writer.Flush(); nextFlush = clock() + 1;
            FlushMilliseconds += (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
        }
        public static void FlushDue() { foreach (var log in Open) log.FlushIfDue(); }
        public static void FlushAll() { foreach (var log in Open) log.Flush(); }
        public void Dispose()
        {
            if (disposed) return;
            Flush(); writer.Dispose(); disposed = true; Open.Remove(this);
        }
    }
}
