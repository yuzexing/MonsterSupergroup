using System;
using UnityEngine;

namespace AstralShift.HellMaiden.AI
{
    [Serializable]
    public struct EnemyActionState
    {
        public ulong ActionId;
        public EnemyAttackPresentationPhase Phase;
        public double WarningStartedAt, WarningUntil, ActiveUntil, RecoveryUntil, NextAttackAt;
        public Vector2 Facing, TargetPosition;
        public Vector2 ProjectileDirection;
        public bool ProjectileEmitted;
        public bool Dash;
        public Vector2 DashStart, DashEnd, DashLastPosition, DashWarningOrigin;

        public EnemyAttackPresentationPhase PhaseAt(double now)
        {
            if (Phase == EnemyAttackPresentationPhase.Cancelled || Phase == EnemyAttackPresentationPhase.Inactive) return Phase;
            // Small inter-client clock differences cannot rewind an accepted phase.
            if (Phase == EnemyAttackPresentationPhase.Warning && now < WarningUntil) return EnemyAttackPresentationPhase.Warning;
            if (Phase != EnemyAttackPresentationPhase.Recovery && now < ActiveUntil) return EnemyAttackPresentationPhase.Active;
            return now < RecoveryUntil ? EnemyAttackPresentationPhase.Recovery : EnemyAttackPresentationPhase.Inactive;
        }
        public double StartAt(EnemyAttackPresentationPhase phase) => phase == EnemyAttackPresentationPhase.Warning
            ? WarningStartedAt : phase == EnemyAttackPresentationPhase.Active ? WarningUntil : ActiveUntil;
        public double EndAt(EnemyAttackPresentationPhase phase) => phase == EnemyAttackPresentationPhase.Warning
            ? WarningUntil : phase == EnemyAttackPresentationPhase.Active ? ActiveUntil : RecoveryUntil;
    }

    [Serializable]
    public struct EnemyKnockbackMotionState
    {
        public bool Active;
        public Vector2 Start, End, LastPosition;
        public float Elapsed, Duration, StaggerDuration;
        public bool RestoreDefaultMovement, RestorePathMovement, RestoreCanBeStuck;
    }
}
