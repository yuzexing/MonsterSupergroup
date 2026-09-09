using System;
using System.Collections.Generic;

namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>Per-player dash charges and deadlines. The caller supplies the authoritative or predicted clock.</summary>
    public sealed class PlayerDashRuntime
    {
        public const double ChainDelaySeconds = 0.15d;
        private readonly List<double> rechargeReadyAt = new List<double>();

        public PlayerDashRuntime(int maxCharges = 0) => ConfigureCapacity(maxCharges);

        public int MaxCharges { get; private set; }
        public int AvailableCharges => Math.Max(0, MaxCharges - rechargeReadyAt.Count);
        public double NextUseAt { get; private set; }

        /// <summary>Changing capacity never deletes the deadlines of already spent charges.</summary>
        public void ConfigureCapacity(int maxCharges)
        {
            if (maxCharges < 0) throw new ArgumentOutOfRangeException(nameof(maxCharges));
            MaxCharges = maxCharges;
        }

        /// <summary>Complete each expired recharge independently; reading properties does not advance time.</summary>
        public bool Refresh(double now)
        {
            ValidateTime(now, nameof(now));
            bool changed = false;
            for (int index = rechargeReadyAt.Count - 1; index >= 0; index--)
            {
                if (rechargeReadyAt[index] > now) continue;
                rechargeReadyAt.RemoveAt(index);
                changed = true;
            }
            return changed;
        }

        public bool TryConsume(double now, double dashDuration, double rechargeSeconds)
        {
            if (!IsTimeValid(now) || !IsTimeValid(dashDuration) || !IsTimeValid(rechargeSeconds)) return false;
            double readyAt = now + rechargeSeconds;
            double nextUseAt = now + dashDuration + ChainDelaySeconds;
            if (!IsTimeValid(readyAt) || !IsTimeValid(nextUseAt) || nextUseAt <= now) return false;
            Refresh(now);
            if (AvailableCharges == 0 || now < NextUseAt) return false;
            rechargeReadyAt.Add(readyAt);
            NextUseAt = nextUseAt;
            return true;
        }

        public PlayerDashSnapshot Capture(double now)
        {
            Refresh(now);
            return new PlayerDashSnapshot
            {
                MaxCharges = MaxCharges,
                NextUseAt = NextUseAt,
                RechargeReadyAt = rechargeReadyAt.ToArray()
            };
        }

        /// <summary>Validate and copy transport data before changing this instance; offline deadlines keep advancing.</summary>
        public void Restore(PlayerDashSnapshot snapshot, double now)
        {
            ValidateTime(now, nameof(now));
            if (snapshot.MaxCharges < 0)
                throw new ArgumentOutOfRangeException(nameof(snapshot), "Dash capacity cannot be negative.");
            ValidateTime(snapshot.NextUseAt, nameof(snapshot));
            double[] deadlines = snapshot.RechargeReadyAt != null
                ? (double[])snapshot.RechargeReadyAt.Clone()
                : Array.Empty<double>();
            foreach (double deadline in deadlines) ValidateTime(deadline, nameof(snapshot));

            MaxCharges = snapshot.MaxCharges;
            NextUseAt = snapshot.NextUseAt;
            rechargeReadyAt.Clear();
            rechargeReadyAt.AddRange(deadlines);
            Refresh(now);
        }

        private static bool IsTimeValid(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;

        private static void ValidateTime(double value, string parameter)
        {
            if (!IsTimeValid(value)) throw new ArgumentOutOfRangeException(parameter, "Dash times must be finite and non-negative.");
        }
    }
}
