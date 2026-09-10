using System;

namespace MonsterSupergroup.NetworkCombat
{
    public enum WavePhase : byte { Disabled, Waiting, Running, Paused, Stopped }

    [Serializable]
    public struct WaveProgressSnapshot
    {
        public string RunId;
        public WavePhase Phase;
        public int Wave;
        public double Elapsed;
        public double WaveDuration;
        public int Planned, Spawned, Skipped, Alive, Limit;
        public long TotalSpawned, TotalSkipped;
        public double Remaining => Wave > 0 ? Math.Max(0, Wave * WaveDuration - Elapsed) : WaveDuration;
    }

    /// <summary>Immutable settings captured once at the server's start-run boundary.</summary>
    public sealed class WaveParameters
    {
        public readonly double Duration, Interval;
        public readonly int Count, Limit, Attempts;
        public readonly float Radius, PlayerClearance;

        public WaveParameters(double duration, int count, double interval, int limit,
            float radius = 5, float playerClearance = 2, int attempts = 16)
        {
            if (!FinitePositive(duration) || !FinitePositive(interval) || count < 1 || count > 10000 ||
                (count - 1) * interval >= duration || limit < 1 ||
                !FinitePositive(radius) || !FinitePositive(playerClearance) || radius < playerClearance ||
                attempts < 1 || attempts > 1024)
                throw new ArgumentException("Wave settings require positive finite values, spawn times inside the wave, and valid placement limits.");
            Duration = duration; Count = count; Interval = interval; Limit = limit;
            Radius = radius; PlayerClearance = playerClearance; Attempts = attempts;
        }
        private static bool FinitePositive(double value) => value > 0 && !double.IsInfinity(value) && !double.IsNaN(value);
    }

    public readonly struct WaveSpawnOpportunity
    {
        public readonly long Sequence;
        public readonly int Wave, Index;
        public WaveSpawnOpportunity(long sequence, int wave, int index)
        { Sequence = sequence; Wave = wave; Index = index; }
    }

    /// <summary>Server clock only. No Unity coroutines, enemies, player registry or network transport.</summary>
    public sealed class ServerWaveSchedule
    {
        private readonly WaveParameters settings;
        private WaveProgressSnapshot state;
        private double lastTime;
        private long nextOpportunity;
        private long pendingSequence;
        public WaveProgressSnapshot State => state;

        public ServerWaveSchedule(string runId, WaveParameters parameters, double now)
        {
            if (string.IsNullOrEmpty(runId)) throw new ArgumentException(nameof(runId));
            settings = parameters ?? throw new ArgumentNullException(nameof(parameters));
            lastTime = now;
            state = new WaveProgressSnapshot { RunId = runId, Phase = WavePhase.Running, Wave = 1,
                WaveDuration = parameters.Duration, Planned = parameters.Count, Limit = parameters.Limit };
        }

        public bool Tick(double now, bool hasActivePlayer, int alive, out WaveSpawnOpportunity opportunity)
        {
            opportunity = default;
            if (state.Phase == WavePhase.Stopped) return false;
            if (pendingSequence != 0) throw new InvalidOperationException("Resolve the previous spawn decision first.");
            double delta = Math.Max(0, now - lastTime);
            lastTime = Math.Max(now, lastTime);
            state.Alive = alive;
            if (!hasActivePlayer) { state.Phase = WavePhase.Paused; return false; }
            if (state.Phase != WavePhase.Paused) state.Elapsed += delta;
            state.Phase = WavePhase.Running;
            int waveIndex = (int)Math.Floor(state.Elapsed / settings.Duration);
            if (state.Wave != waveIndex + 1)
            {
                state.Wave = waveIndex + 1;
                state.Spawned = state.Skipped = 0;
            }
            double within = state.Elapsed - waveIndex * settings.Duration;
            long start = (long)waveIndex * settings.Count;
            long due = start + Math.Min(settings.Count, (long)Math.Floor((within + 1e-7) / settings.Interval) + 1);
            if (due <= nextOpportunity) return false;
            // A slow frame consumes only the most recent opportunity. Earlier slots never burst later.
            long missed = due - nextOpportunity - 1;
            state.TotalSkipped += missed;
            state.Skipped += (int)Math.Max(0, due - 1 - Math.Max(start, nextOpportunity));
            nextOpportunity = due;
            pendingSequence = due;
            opportunity = new WaveSpawnOpportunity(due, state.Wave, (int)(due - start));
            return true;
        }

        public bool Resolve(WaveSpawnOpportunity opportunity, bool spawned)
        {
            if (state.Phase == WavePhase.Stopped || pendingSequence == 0 || opportunity.Sequence != pendingSequence)
                return false;
            pendingSequence = 0;
            if (spawned) { state.Spawned++; state.TotalSpawned++; state.Alive++; }
            else { state.Skipped++; state.TotalSkipped++; }
            return true;
        }

        public void Stop() { pendingSequence = 0; state.Phase = WavePhase.Stopped; }
    }
}
