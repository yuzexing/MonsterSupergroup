using System;
using System.Collections.Generic;
using UnityEngine;

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
        public readonly WaveSpawnProgram Program;
        public IReadOnlyList<GameObject> Prefabs { get; }

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
            var events = new WaveSpawnEntry[count];
            for (int i = 0; i < count; i++) events[i] = new WaveSpawnEntry(i * interval, 0);
            Program = new WaveSpawnProgram(duration, duration, events);
            Prefabs = Array.AsReadOnly(Array.Empty<GameObject>());
        }
        public WaveParameters(WaveSpawnProgram program, GameObject[] prefabs, int limit,
            float radius = 5, float playerClearance = 2, int attempts = 16)
            : this(program?.WaveDuration ?? 0, 1, program?.WaveDuration ?? 0, limit, radius, playerClearance, attempts)
        {
            Program = program; Count = program.CountInWave(1); Interval = 0;
            Prefabs = Array.AsReadOnly((GameObject[])(prefabs ?? throw new ArgumentNullException(nameof(prefabs))).Clone());
        }
        private static bool FinitePositive(double value) => value > 0 && !double.IsInfinity(value) && !double.IsNaN(value);
    }

    public readonly struct WaveSpawnOpportunity
    {
        public readonly long Sequence;
        public readonly int Wave, Index;
        public readonly int PrefabIndex;
        public readonly double ScheduledTime;
        public WaveSpawnOpportunity(long sequence, int wave, int index)
            : this(sequence, wave, index, 0, 0) { }
        public WaveSpawnOpportunity(long sequence, int wave, int index, int prefabIndex, double time)
        { Sequence = sequence; Wave = wave; Index = index; PrefabIndex = prefabIndex; ScheduledTime = time; }
    }

    /// <summary>Server clock only. No Unity coroutines, enemies, player registry or network transport.</summary>
    public sealed class ServerWaveSchedule
    {
        private readonly WaveParameters settings;
        private WaveProgressSnapshot state;
        private double lastTime;
        private long nextOpportunity;
        private long pendingSequence;
        private long groupEnd;
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
            state.Alive = alive;
            // Drain one authored simultaneous group in the same server update.
            if (nextOpportunity < groupEnd)
            {
                pendingSequence = nextOpportunity + 1;
                opportunity = settings.Program.Get(pendingSequence);
                return true;
            }
            double delta = Math.Max(0, now - lastTime);
            lastTime = Math.Max(now, lastTime);
            if (!hasActivePlayer) { state.Phase = WavePhase.Paused; return false; }
            if (state.Phase != WavePhase.Paused) state.Elapsed += delta;
            state.Phase = WavePhase.Running;
            int waveIndex = (int)Math.Floor((state.Elapsed + WaveSpawnProgram.Epsilon) / settings.Duration);
            if (state.Wave != waveIndex + 1)
            {
                state.Wave = waveIndex + 1;
                state.Spawned = state.Skipped = 0;
                state.Planned = settings.Program.CountInWave(state.Wave);
            }
            long start = settings.Program.EventsBeforeWave(state.Wave);
            long due = settings.Program.LastDue(state.Elapsed);
            if (due <= nextOpportunity) return false;
            // Never revive the previous wave's last spawn during an empty lead-in.
            bool currentWave = settings.Program.Get(due).Wave == state.Wave;
            long first = currentWave ? Math.Max(nextOpportunity + 1, settings.Program.FirstInGroup(due)) : due + 1;
            long missed = first - nextOpportunity - 1;
            state.TotalSkipped += missed;
            state.Skipped += (int)Math.Max(0, first - 1 - Math.Max(start, nextOpportunity));
            nextOpportunity = first - 1;
            groupEnd = due;
            if (!currentWave) return false;
            pendingSequence = first;
            opportunity = settings.Program.Get(first);
            return true;
        }

        public bool Resolve(WaveSpawnOpportunity opportunity, bool spawned)
        {
            if (state.Phase == WavePhase.Stopped || pendingSequence == 0 || opportunity.Sequence != pendingSequence)
                return false;
            pendingSequence = 0;
            nextOpportunity = opportunity.Sequence;
            if (spawned) { state.Spawned++; state.TotalSpawned++; state.Alive++; }
            else { state.Skipped++; state.TotalSkipped++; }
            return true;
        }

        public void Stop() { pendingSequence = groupEnd = 0; state.Phase = WavePhase.Stopped; }
    }
}
