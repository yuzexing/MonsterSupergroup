using System;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class ServerWaveSchedule
    {
        private sealed class BarrierClock
        {
            public bool Started, Spawned;
            public double LastUpdate;
            public float Chance;
        }
        private BarrierClock[] barrierClocks;
        private int barrierFrame = -1;
        public event Action<int, string, float, float, float> ReferenceBarrierDecision;

        // Called once after the normal timeline tick. A failed Init can be followed by
        // Progress in the same frame; subsequent retries are one second apart, with no catch-up.
        // Configured end controls probability, not an unconditional despawn deadline.
        public void TickReferenceBarriers(int frame, Func<float> percentRoll, Func<int, bool> spawn)
        {
            if ((state.Phase != WavePhase.Running && state.Phase != WavePhase.TransitionPending) || referencePaused || settings.Reference == null || barrierFrame == frame) return;
            barrierFrame = frame;
            var definitions = settings.Reference.Barriers;
            if (barrierClocks == null)
            {
                barrierClocks = new BarrierClock[definitions.Length];
                for (int i = 0; i < definitions.Length; i++) barrierClocks[i] = new BarrierClock();
            }
            for (int i = 0; i < definitions.Length; i++)
            {
                var data = definitions[i]; var clock = barrierClocks[i];
                if (data.start >= settings.Reference.EndTime || state.Elapsed <= data.start || clock.Spawned ||
                    state.TransitionRequestedAt > 0 && !clock.Started) continue;
                float increment = 100f / (float)(data.end - data.start - data.shrinkDuration);
                if (!clock.Started)
                {
                    clock.Started = true; clock.Chance = increment;
                    AttemptBarrier(i, clock, increment, percentRoll, spawn);
                }
                if (!clock.Spawned && state.Elapsed - clock.LastUpdate >= 1)
                {
                    clock.LastUpdate = state.Elapsed;
                    AttemptBarrier(i, clock, increment, percentRoll, spawn);
                }
            }
        }

        private void AttemptBarrier(int index, BarrierClock clock, float increment, Func<float> roll, Func<int, bool> spawn)
        {
            if (trapCount == 1) { ReferenceBarrierDecision?.Invoke(index, "Occupied", clock.Chance, clock.Chance, float.NaN); return; }
            float value = roll(), chance = clock.Chance;
            if (value > chance) { clock.Chance += increment; ReferenceBarrierDecision?.Invoke(index, "RollFailed", chance, clock.Chance, value); return; }
            if (!TryAcquireBarrierSlot()) return;
            if (spawn(index)) { clock.Spawned = true; ReferenceBarrierDecision?.Invoke(index, "Spawned", chance, chance, value); }
            else { ReleaseBarrierSlot(); ReferenceBarrierDecision?.Invoke(index, "PositionFailed", chance, chance, value); }
        }
    }
}
