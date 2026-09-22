using System;

namespace MonsterSupergroup.NetworkCombat
{
    public enum MusicEffect : byte { Push, Lightning, Speed, Finale }

    [Serializable]
    public struct MusicParameters
    {
        public float Bpm, HitWindowSeconds, Cooldown;
        public int CountInBeats, BeatCount;
        public float PushRadius, PushDistance, PushDuration;
        public float LightningRadius, LightningDamage;
        public int LightningTargets;
        public float SpeedBonus, SpeedDuration, FinaleRadius, FinaleDamage, FeedbackVolume;
        public double BeatInterval => 60d / Bpm;
        public double BeatTime(int index) => (CountInBeats + index) * BeatInterval;
        public double EndTime => BeatTime(BeatCount - 1) + HitWindowSeconds;
        public static MusicParameters Defaults => new MusicParameters
        {
            Bpm = 120, CountInBeats = 2, BeatCount = 10, HitWindowSeconds = .12f, Cooldown = 20,
            PushRadius = 4, PushDistance = 2, PushDuration = .25f,
            LightningRadius = 6, LightningTargets = 3, LightningDamage = 20,
            SpeedBonus = .25f, SpeedDuration = 2, FinaleRadius = 6, FinaleDamage = 60, FeedbackVolume = .3f
        };
        public bool IsValid => Range(Bpm, 60, 240) && CountInBeats >= 1 && CountInBeats <= 4 && BeatCount == 10 &&
            Range(HitWindowSeconds, .03f, .2f) && HitWindowSeconds < BeatInterval / 2 && Range(Cooldown, .1f, 120) &&
            Range(PushRadius, .1f, 20) && Range(PushDistance, .1f, 10) && Range(PushDuration, .05f, 2) &&
            Range(LightningRadius, .1f, 20) && LightningTargets >= 1 && LightningTargets <= 32 && Range(LightningDamage, 0, 10000) &&
            Range(SpeedBonus, 0, 2) && Range(SpeedDuration, .1f, 20) && Range(FinaleRadius, .1f, 20) &&
            Range(FinaleDamage, 0, 10000) && Range(FeedbackVolume, 0, 1);
        private static bool Range(float n, float min, float max) => MusicTiming.Finite(n) && n >= min && n <= max;
    }

    public static class MusicTiming
    {
        // Each beat owns one half-open input slot. Repeated presses within it cannot consume a later beat.
        public static int InputIndex(MusicParameters p, double elapsed)
        {
            if (!p.IsValid || !Finite(elapsed)) return -1;
            double index = Math.Floor((elapsed - p.BeatTime(0)) / p.BeatInterval + .5d);
            return index >= 0 && index < p.BeatCount ? (int)index : -1;
        }
        public static bool IsHit(MusicParameters p, int index, double elapsed) =>
            index >= 0 && index < p.BeatCount && Finite(elapsed) &&
            Math.Abs(elapsed - p.BeatTime(index)) <= p.HitWindowSeconds + .000001d;
        public static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        public static int Count(uint mask) { int n = 0; while (mask != 0) { n += (int)(mask & 1); mask >>= 1; } return n; }
    }

    [Serializable]
    public struct MusicSnapshot
    {
        public uint Revision;
        public ulong CastId;
        public bool Active, Scheduled, Cancelled;
        public double StartedAt, CooldownReadyAt;
        public uint JudgedMask, HitMask;
        public int Hits => MusicTiming.Count(HitMask);
        public int Judged => MusicTiming.Count(JudgedMask);
    }

    /// <summary>One ten-beat cast. Input time is the owner's calibrated DSP time, never packet arrival time.</summary>
    public sealed class MusicPrototypeRuntime
    {
        public const double ScheduleTimeout = 3;
        public const double ReportGrace = 1.5;
        public const double FutureTolerance = .35;
        private MusicSnapshot state;
        private MusicParameters parameters;
        private double admittedAt;
        public MusicSnapshot State => state;
        public MusicParameters Parameters => parameters;
        public bool CanBegin(MusicParameters p, double now) => p.IsValid && MusicTiming.Finite(now) && !state.Active && now >= state.CooldownReadyAt;
        public bool TryBegin(ulong cast, MusicParameters p, double now)
        {
            if (cast == 0 || !CanBegin(p, now)) return false;
            uint revision = state.Revision;
            state = new MusicSnapshot { Revision = revision, CastId = cast, Active = true };
            parameters = p; admittedAt = now; Changed(); return true;
        }
        public bool TrySchedule(ulong cast, double start, double now)
        {
            if (!state.Active || state.Scheduled || cast != state.CastId || !MusicTiming.Finite(start) || !MusicTiming.Finite(now) ||
                now > admittedAt + ScheduleTimeout || start < admittedAt || start > now + FutureTolerance || start < now - ReportGrace) return false;
            state.StartedAt = start; state.Scheduled = true; Changed(); return true;
        }
        public bool TryJudge(ulong cast, int index, double elapsed, double now, out bool hit)
        {
            hit = false;
            if (!state.Active || !state.Scheduled || cast != state.CastId || index < 0 || index >= parameters.BeatCount ||
                !MusicTiming.Finite(elapsed) || !MusicTiming.Finite(now) || (state.JudgedMask & (1u << index)) != 0 ||
                MusicTiming.InputIndex(parameters, elapsed) != index) return false;
            double inputAt = state.StartedAt + elapsed;
            // Bound clock claims and stale traffic, while preserving the original timing judgement under latency.
            if (inputAt > now + FutureTolerance || inputAt < now - ReportGrace ||
                now > state.StartedAt + parameters.BeatTime(index) + parameters.HitWindowSeconds + ReportGrace) return false;
            state.JudgedMask |= 1u << index;
            hit = MusicTiming.IsHit(parameters, index, elapsed);
            if (hit) state.HitMask |= 1u << index;
            Changed();
            if (state.Judged == parameters.BeatCount) Finish(now, false);
            return true;
        }
        public bool Advance(double now)
        {
            if (!state.Active || !MusicTiming.Finite(now)) return false;
            if (!state.Scheduled)
            {
                if (now < admittedAt + ScheduleTimeout) return false;
                Finish(now, true); return true;
            }
            uint before = state.JudgedMask;
            for (int i = 0; i < parameters.BeatCount; i++)
                if (now > state.StartedAt + parameters.BeatTime(i) + parameters.HitWindowSeconds + ReportGrace)
                    state.JudgedMask |= 1u << i;
            if (before == state.JudgedMask) return false;
            Changed();
            if (state.Judged == parameters.BeatCount) Finish(now, false);
            return true;
        }
        public bool Cancel(double now)
        {
            if (!state.Active || !MusicTiming.Finite(now)) return false;
            Finish(now, true); return true;
        }
        public void ResetCooldown() { state.CooldownReadyAt = 0; Changed(); }
        private void Finish(double now, bool cancelled)
        {
            state.Active = false; state.Cancelled = cancelled; state.CooldownReadyAt = now + parameters.Cooldown; Changed();
        }
        private void Changed() => state.Revision = state.Revision == uint.MaxValue ? 1 : state.Revision + 1;
    }
}
