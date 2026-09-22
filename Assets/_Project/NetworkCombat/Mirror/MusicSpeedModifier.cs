using System;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Player;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>One independently expiring move-speed modifier, outside the player's permanent build.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMovement))]
    public sealed class MusicSpeedModifier : MonoBehaviour
    {
        private PlayerMovement player;
        private PlayerStats appliedStats;
        private readonly TemporaryMoveSpeed modifier = new TemporaryMoveSpeed();
        private double expiresAt;
        public double Remaining => Math.Max(0, expiresAt - NetworkTime.time);
        public float Bonus => Remaining > 0 ? modifier.Bonus : 0;

        private void Awake() => player = GetComponent<PlayerMovement>();

        public void Apply(float bonus, double until)
        {
            if (float.IsNaN(bonus) || float.IsInfinity(bonus) || bonus < 0 ||
                double.IsNaN(until) || double.IsInfinity(until))
                throw new ArgumentOutOfRangeException(nameof(bonus));
            if (until <= NetworkTime.time || bonus == 0) { Clear(); return; }
            bool changed = expiresAt != until || modifier.Bonus != bonus;
            expiresAt = until;
            modifier.Bonus = bonus;
            bool attached = EnsureApplied();
            if (changed && !attached) appliedStats?.EvaluateModifiers();
        }

        private void Update()
        {
            if (expiresAt == 0) return;
            if (NetworkTime.time >= expiresAt) { Clear(); return; }
            EnsureApplied();
        }

        private bool EnsureApplied()
        {
            if (player == null) player = GetComponent<PlayerMovement>();
            if (player == null || !player.IsRuntimeInitialized || player.PlayerStats == null) return false;
            if (ReferenceEquals(appliedStats, player.PlayerStats)) return false;
            appliedStats?.RemoveModifier(modifier);
            appliedStats = player.PlayerStats;
            appliedStats.AddModifier(modifier);
            return true;
        }

        public void Clear()
        {
            expiresAt = 0;
            appliedStats?.RemoveModifier(modifier);
            appliedStats = null;
            modifier.Bonus = 0;
        }

        private void OnDisable() => Clear();

        private sealed class TemporaryMoveSpeed : PlayerPerkModifier
        {
            public float Bonus;
            public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
                => multipliers.moveSpeedMultiplier += Bonus;
            public override bool TryStack(RuntimePerkModifier other) => false;
        }
    }
}
