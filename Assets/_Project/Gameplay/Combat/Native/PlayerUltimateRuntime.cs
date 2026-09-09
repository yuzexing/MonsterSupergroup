using System;

namespace MonsterSupergroup.Gameplay.Combat
{
    [Serializable]
    public struct PlayerUltimateSnapshot
    {
        public bool HasCharge;
        public double ActiveUntil;
        public double InvulnerableUntil;
    }

    /// <summary>One held charge and absolute session deadlines. It owns neither attack objects nor input.</summary>
    public sealed class PlayerUltimateRuntime
    {
        private PlayerUltimateSnapshot state;
        public bool HasCharge => state.HasCharge;
        public bool TryGrantCharge()
        {
            if (state.HasCharge) return false;
            state.HasCharge = true;
            return true;
        }
        public bool CanUse(double now)
        { ValidateTime(now); return state.HasCharge && now >= state.ActiveUntil; }
        public bool IsInvulnerable(double now)
        { ValidateTime(now); return now < state.InvulnerableUntil; }

        public bool TryConsume(double now, double sequenceSeconds, double invulnerabilitySeconds)
        {
            ValidateTime(now); ValidateTime(sequenceSeconds); ValidateTime(invulnerabilitySeconds);
            if (sequenceSeconds <= 0 || invulnerabilitySeconds > sequenceSeconds)
                throw new ArgumentOutOfRangeException(nameof(sequenceSeconds));
            ValidateTime(now + sequenceSeconds); ValidateTime(now + invulnerabilitySeconds);
            if (!CanUse(now)) return false;
            state.HasCharge = false;
            state.ActiveUntil = now + sequenceSeconds;
            state.InvulnerableUntil = now + invulnerabilitySeconds;
            return true;
        }
        public PlayerUltimateSnapshot Capture() => state;
        public void Restore(PlayerUltimateSnapshot snapshot)
        {
            ValidateTime(snapshot.ActiveUntil); ValidateTime(snapshot.InvulnerableUntil);
            if (snapshot.InvulnerableUntil > snapshot.ActiveUntil)
                throw new ArgumentException("Ultimate protection cannot outlive its admitted use.", nameof(snapshot));
            state = snapshot;
        }
        private static void ValidateTime(double time)
        {
            if (double.IsNaN(time) || double.IsInfinity(time) || time < 0)
                throw new ArgumentOutOfRangeException(nameof(time));
        }
    }
}
