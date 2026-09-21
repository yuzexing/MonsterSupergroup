using System;
using System.Collections.Generic;

namespace MonsterSupergroup.NetworkCombat
{
    public readonly struct WaveSpawnEntry
    {
        public readonly double Time;
        public readonly int PrefabIndex, DefinitionIndex;
        public WaveSpawnEntry(double time, int prefabIndex, int definitionIndex = -1)
        { Time = time; PrefabIndex = prefabIndex; DefinitionIndex = definitionIndex; }
    }

    /// <summary>Finite authored events followed by repetitions of the last wave. No runtime clock or Unity objects.</summary>
    public sealed class WaveSpawnProgram
    {
        internal const double Epsilon = 1e-7;
        private readonly WaveSpawnEntry[] entries;
        private readonly int[] beforeWave;
        private readonly int tailStart;
        public double WaveDuration { get; }
        public double Duration { get; }
        public int WaveCount { get; }
        public int EventCount => entries.Length;
        public int TailCount => entries.Length - tailStart;

        public WaveSpawnProgram(double waveDuration, double duration, IEnumerable<WaveSpawnEntry> events)
        {
            if (!Positive(waveDuration) || !Positive(duration) || duration / waveDuration > 10000 ||
                Math.Abs(duration / waveDuration - Math.Round(duration / waveDuration)) > Epsilon || duration < waveDuration)
                throw new ArgumentException("Timeline length must be a positive whole number of waves (at most 10000).");
            WaveDuration = waveDuration; Duration = duration; WaveCount = (int)Math.Round(duration / waveDuration);
            entries = new List<WaveSpawnEntry>(events ?? throw new ArgumentNullException(nameof(events))).ToArray();
            if (entries.Length > 100000) throw new ArgumentException("Timeline exceeds 100000 spawn events.");
            beforeWave = new int[WaveCount + 1];
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (double.IsNaN(entry.Time) || double.IsInfinity(entry.Time) || entry.Time < 0 || entry.Time >= duration ||
                    entry.PrefabIndex < 0 || (i > 0 && entries[i - 1].Time > entry.Time))
                    throw new ArgumentException("Spawn events must be finite, ordered and inside the Timeline.");
                int wave = Math.Min(WaveCount - 1, (int)Math.Floor((entry.Time + Epsilon) / waveDuration));
                beforeWave[wave + 1]++;
            }
            for (int i = 1; i < beforeWave.Length; i++) beforeWave[i] += beforeWave[i - 1];
            tailStart = beforeWave[WaveCount - 1];
        }

        public int CountInWave(int wave) => wave <= WaveCount
            ? beforeWave[wave] - beforeWave[wave - 1] : TailCount;
        public long EventsBeforeWave(int wave) => wave <= WaveCount
            ? beforeWave[wave - 1] : entries.Length + (long)(wave - WaveCount - 1) * TailCount;

        public WaveSpawnOpportunity Get(long sequence)
        {
            if (sequence <= 0 || (sequence > entries.Length && TailCount == 0)) throw new ArgumentOutOfRangeException(nameof(sequence));
            WaveSpawnEntry entry; double time;
            if (sequence <= entries.Length) { entry = entries[sequence - 1]; time = entry.Time; }
            else
            {
                long offset = sequence - entries.Length - 1;
                entry = entries[tailStart + (int)(offset % TailCount)];
                time = Duration + (offset / TailCount) * WaveDuration + entry.Time - (Duration - WaveDuration);
            }
            int wave = checked((int)Math.Floor((time + Epsilon) / WaveDuration) + 1);
            return new WaveSpawnOpportunity(sequence, wave, (int)(sequence - EventsBeforeWave(wave)), entry.PrefabIndex, time, -1, -1, entry.DefinitionIndex);
        }

        public long LastDue(double elapsed)
        {
            if (entries.Length == 0) return 0;
            if (elapsed < Duration - Epsilon || TailCount == 0) return UpperBound(elapsed) + 1;
            long cycle = Math.Max(0, (long)Math.Floor((elapsed - Duration + Epsilon) / WaveDuration));
            double local = Duration - WaveDuration + elapsed - Duration - cycle * WaveDuration;
            int index = UpperBound(local);
            return entries.Length + cycle * TailCount + Math.Max(0, index - tailStart + 1);
        }

        public long FirstInGroup(long last)
        {
            double time = Get(last).ScheduledTime;
            while (last > 1 && Math.Abs(Get(last - 1).ScheduledTime - time) <= Epsilon) last--;
            return last;
        }

        private int UpperBound(double time)
        {
            int lo = 0, hi = entries.Length;
            while (lo < hi) { int mid = lo + (hi - lo) / 2; if (entries[mid].Time <= time + Epsilon) lo = mid + 1; else hi = mid; }
            return lo - 1;
        }
        private static bool Positive(double value) => value > 0 && !double.IsInfinity(value) && !double.IsNaN(value);
    }
}
