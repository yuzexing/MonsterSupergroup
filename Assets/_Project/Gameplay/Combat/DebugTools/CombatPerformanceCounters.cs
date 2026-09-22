using System;
using System.Diagnostics;
using Unity.Profiling;

namespace AstralShift.DebugTools
{
    // Main-thread counters. Scopes are nested: their durations must not be added together.
    public static class CombatPerformanceCounters
    {
        public enum Area { CollisionChecks, DamageRequests, StateTransitions, SnapshotSend, SnapshotReceive }
        public static readonly string[] Names = Enum.GetNames(typeof(Area));
        private static readonly ProfilerMarker[] Markers = {
            new("Combat.CollisionChecks"), new("Combat.DamageRequests"), new("Combat.StateTransitions"),
            new("Combat.SnapshotSend"), new("Combat.SnapshotReceive") };
        private static readonly long[] Calls = new long[Names.Length], Ticks = new long[Names.Length];
        public static bool Enabled { get; set; }
        private static long deadDamageRequests, invalidDeadTransitions;
        public static Scope Measure(Area area) => Enabled ? new Scope((int)area) : default;
        public static void DeadDamageRequest() { if (Enabled) deadDamageRequests++; }
        public static void InvalidDeadTransition() { if (Enabled) invalidDeadTransitions++; }
        public static void Reset() { Array.Clear(Calls, 0, Calls.Length); Array.Clear(Ticks, 0, Ticks.Length); deadDamageRequests = invalidDeadTransitions = 0; }
        public static Sample ReadAndReset()
        {
            var sample = new Sample { calls = (long[])Calls.Clone(), milliseconds = new double[Names.Length],
                deadDamageRequests = deadDamageRequests, invalidDeadTransitions = invalidDeadTransitions };
            for (int i = 0; i < Ticks.Length; i++) sample.milliseconds[i] = Ticks[i] * 1000d / Stopwatch.Frequency;
            Reset(); return sample;
        }
        [Serializable] public sealed class Sample
        {
            public long[] calls;
            public double[] milliseconds;
            public long deadDamageRequests, invalidDeadTransitions;
        }
        public readonly struct Scope : IDisposable
        {
            private readonly int index;
            private readonly long start;
            private readonly bool active;
            internal Scope(int index) { this.index = index; active = true; start = Stopwatch.GetTimestamp(); Markers[index].Begin(); }
            public void Dispose()
            {
                if (!active) return;
                Markers[index].End(); Calls[index]++; Ticks[index] += Stopwatch.GetTimestamp() - start;
            }
        }
    }
}
