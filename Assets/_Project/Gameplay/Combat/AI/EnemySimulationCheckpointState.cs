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
        public bool Explosion, ExplosionTriggered, SelfDestructPending;
        public Vector2 ExplosionPosition;
        public bool Sequence;
        public double ComboStartedAt;
        public Vector3 SequenceWarnings, SequenceActives;
        public int StrikeIndex, PoseStrikeIndex;
        public byte LockedStrikeMask, ExecutedStrikeMask;

        public EnemyAttackPresentationPhase PhaseAt(double now)
        {
            return EnemyActionTimeline.Resolve(this, now).Phase;
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
