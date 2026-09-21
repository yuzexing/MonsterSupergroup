using System;
using System.Collections.Generic;

namespace MonsterSupergroup.NetworkCombat
{
    [Serializable]
    public struct GluttonyParameters
    {
        public bool Enabled, PassiveEnabled, ActiveEnabled;
        public float PassiveCooldown, ActiveCooldown, Radius, Length, Width;
        public float MarkDuration, ScanInterval, FlashDuration, FeedbackVolume;
        public int MaximumTargets;
        public static GluttonyParameters Defaults => new GluttonyParameters
        {
            Enabled = false, PassiveEnabled = true, ActiveEnabled = true,
            PassiveCooldown = 10, ActiveCooldown = 20, Radius = 1.15f,
            Length = 5.75f, Width = 2.3f, MarkDuration = 6, MaximumTargets = 5,
            ScanInterval = .05f, FlashDuration = .2f, FeedbackVolume = .12f
        };
        public bool IsValid => Range(PassiveCooldown, .1f, 120) && Range(ActiveCooldown, .1f, 120) &&
            Range(Radius, .1f, 5) && Range(Length, .2f, 30) && Range(Width, .1f, 15) &&
            Range(MarkDuration, .2f, 60) && MaximumTargets >= 1 && MaximumTargets <= 32 &&
            Range(ScanInterval, .02f, .25f) && Range(FlashDuration, .05f, 1) && Range(FeedbackVolume, 0, 1);
        private static bool Range(float v, float min, float max) => !float.IsNaN(v) && !float.IsInfinity(v) && v >= min && v <= max;
    }

    [Serializable]
    public struct GluttonySnapshot
    {
        public uint Revision;
        public double PassiveReadyAt, ActiveReadyAt, MarkExpiresAt;
        public ulong CastId;
        public int Marked, Collected, Lost, Expired, Cancelled;
        public int PassiveKills, TotalCollected;
    }

    /// <summary>One atomic authoritative reply: counters and target membership share a revision.</summary>
    [Serializable]
    public struct GluttonyReplica
    {
        public GluttonySnapshot State;
        public uint[] Targets;
        public bool HasMark(uint target, double now) => State.CastId != 0 &&
            !double.IsNaN(now) && !double.IsInfinity(now) && now < State.MarkExpiresAt &&
            Targets != null && Array.IndexOf(Targets, target) >= 0;
        public static bool IsNewer(uint candidate, uint current) => candidate != 0 &&
            (current == 0 || unchecked((int)(candidate - current)) > 0);
        public static GluttonyReplica Latest(GluttonyReplica replicated, GluttonyReplica receipt) =>
            IsNewer(receipt.State.Revision, replicated.State.Revision) ? receipt : replicated;
    }

    /// <summary>Prototype rules only. No physics, enemy state, transport, or damage simulation.</summary>
    public sealed class GluttonyPrototypeRuntime
    {
        private readonly HashSet<uint> marks = new HashSet<uint>();
        private GluttonySnapshot state;
        public GluttonySnapshot State => state;
        public IReadOnlyCollection<uint> Marks => marks;
        public bool HasMark(uint target, ulong cast, double now) => cast != 0 && cast == state.CastId &&
            Finite(now) && now < state.MarkExpiresAt && marks.Contains(target);
        public bool CanCast(GluttonyParameters p, double now) => p.IsValid && p.Enabled && p.ActiveEnabled &&
            Finite(now) && now >= state.ActiveReadyAt;

        // Input is already ordered by distance by the geometry adapter. First valid unique targets win.
        public bool TryCast(ulong cast, IEnumerable<uint> targets, GluttonyParameters p, double now)
        {
            if (cast == 0 || targets == null || !CanCast(p, now)) return false;
            CancelMarks();
            state.CastId = cast; state.MarkExpiresAt = now + p.MarkDuration;
            state.ActiveReadyAt = now + p.ActiveCooldown;
            state.Marked = state.Collected = state.Lost = state.Expired = state.Cancelled = 0;
            foreach (uint id in targets)
            {
                if (id == 0 || marks.Count >= p.MaximumTargets) continue;
                marks.Add(id);
            }
            state.Marked = marks.Count;
            return true;
        }
        public bool CanConsume(uint target, bool marked, ulong cast, GluttonyParameters p, double now)
        {
            if (target == 0 || !p.IsValid || !p.Enabled || !Finite(now)) return false;
            return marked ? p.ActiveEnabled && HasMark(target, cast, now) :
                p.PassiveEnabled && now >= state.PassiveReadyAt && !HasMark(target, state.CastId, now);
        }
        // Invoke only after a validated target has been committed by the canonical ledger.
        public void CommitConsume(uint target, bool marked, GluttonyParameters p, double now)
        {
            if (marked)
            {
                if (!marks.Remove(target)) throw new InvalidOperationException("The consumed mark is missing.");
                state.Collected++; state.TotalCollected++;
            }
            else { state.PassiveReadyAt = now + p.PassiveCooldown; state.PassiveKills++; }
        }
        public bool ForgetKilledTarget(uint target)
        {
            if (!marks.Remove(target)) return false;
            state.Lost++; return true;
        }
        public bool Expire(double now)
        {
            if (!Finite(now) || now < state.MarkExpiresAt || marks.Count == 0) return false;
            state.Expired += marks.Count; marks.Clear(); return true;
        }
        public void CancelMarks() { state.Cancelled += marks.Count; marks.Clear(); }
        public void ResetCooldowns() { state.PassiveReadyAt = state.ActiveReadyAt = 0; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
