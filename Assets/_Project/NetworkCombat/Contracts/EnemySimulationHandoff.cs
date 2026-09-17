using System;
using AstralShift.HellMaiden.AI;

namespace MonsterSupergroup.NetworkCombat
{
    public enum EnemyTargetChangeReason : byte { Spawn, TargetDowned, TargetDisconnected, TargetUnavailable, Forced, Resume, Timeout, SimulatorReady, ReferenceReposition }
    public enum EnemyTargetChangeResult : byte { Accepted, Unchanged, UnknownEnemy, EnemyDead, InvalidTarget, NotServer }

    [Serializable]
    public struct EnemySimulationCheckpoint
    {
        public EnemySimulationSnapshot Movement;
    }

    [Serializable]
    public struct EnemySimulationRuntimeState
    {
        public EnemyActionState Action;
        public EnemyKnockbackMotionState Knockback;
        public EnemyKnockbackSettings KnockbackSettings;
        public ulong KnockbackCommandId, KnockbackDamageEventId, LastHandledKnockbackId;
        public EnemyPredictedKnockbackReceipt[] PredictedKnockbacks;

        public bool IsFinite => (byte)Action.Phase <= (byte)EnemyAttackPresentationPhase.Cancelled &&
            EnemyKnockbackSettings.Finite(Action.WarningStartedAt) && EnemyKnockbackSettings.Finite(Action.WarningUntil) &&
            EnemyKnockbackSettings.Finite(Action.ActiveUntil) && EnemyKnockbackSettings.Finite(Action.RecoveryUntil) &&
            EnemyKnockbackSettings.Finite(Action.NextAttackAt) && Finite(Action.Facing) && Finite(Action.TargetPosition) && Finite(Action.ProjectileDirection) &&
            Finite(Action.ExplosionPosition) && (!Action.SelfDestructPending || Action.Explosion && Action.ExplosionTriggered) && ValidSequence && ValidWarningStep &&
            (!Action.Dash || (Finite(Action.DashStart) && Finite(Action.DashEnd) && Finite(Action.DashLastPosition) && Finite(Action.DashWarningOrigin) &&
                Action.WarningUntil > Action.WarningStartedAt && Action.ActiveUntil > Action.WarningUntil && Action.RecoveryUntil >= Action.ActiveUntil)) && ValidReceipts &&
            (!Knockback.Active || (KnockbackSettings.IsValid && Finite(Knockback.Start) && Finite(Knockback.End) &&
            Finite(Knockback.LastPosition) && EnemyKnockbackSettings.Finite(Knockback.Elapsed) && Knockback.Elapsed >= 0 &&
            EnemyKnockbackSettings.Finite(Knockback.Duration) && Knockback.Duration > 0 &&
            EnemyKnockbackSettings.Finite(Knockback.StaggerDuration) && Knockback.StaggerDuration >= 0));
        private static bool Finite(UnityEngine.Vector2 value) => EnemyKnockbackSettings.Finite(value.x) && EnemyKnockbackSettings.Finite(value.y);
        private bool ValidSequence => !Action.Sequence ||
            (EnemyKnockbackSettings.Finite(Action.ComboStartedAt) && Action.StrikeIndex>=0 && Action.StrikeIndex<3 &&
             Action.PoseStrikeIndex>=0 && Action.PoseStrikeIndex<3 && Action.LockedStrikeMask<=7 && Action.ExecutedStrikeMask<=7 &&
             Positive(Action.SequenceWarnings) && Positive(Action.SequenceActives) && Action.RecoveryUntil>=Action.ActiveUntil);
        private bool ValidWarningStep => EnemyKnockbackSettings.Finite(Action.WarningStep.SampledAt) &&
            Finite(Action.WarningStep.RequestedPosition) && Finite(Action.WarningStep.Facing) &&
            Action.WarningStep.StartedMask <= 7 && Action.WarningStep.CompletedMask <= 7 &&
            (!Action.WarningStep.Enabled || Action.Sequence && Action.ActionId != 0 &&
                Action.WarningStep.SampledAt >= Action.ComboStartedAt) &&
            (!Action.WarningStep.Pending || Action.WarningStep.Enabled && Action.WarningStep.StartedMask != 0);
        private static bool Positive(UnityEngine.Vector3 value) =>
            EnemyKnockbackSettings.Finite(value.x) && EnemyKnockbackSettings.Finite(value.y) && EnemyKnockbackSettings.Finite(value.z) && value.x>0 && value.y>0 && value.z>0;
        private bool ValidReceipts
        {
            get
            {
                if (PredictedKnockbacks == null) return true;
                if (PredictedKnockbacks.Length > EnemyPredictedKnockbackHistory.Capacity) return false;
                foreach (var value in PredictedKnockbacks)
                    if (value.DamageEventId == 0 || !EnemyKnockbackSettings.Finite(value.ExpiresAt)) return false;
                return true;
            }
        }
    }

    [Serializable]
    public struct EnemySimulationHandoff
    {
        public EnemySimulationAssignment Assignment;
        public EnemySimulationCheckpoint Checkpoint;
        public EnemyTargetChangeReason Reason;
        public double CommittedAt;
    }

    [Serializable]
    public struct EnemyHandoffDiagnostics
    {
        public uint RequestedTarget;
        public bool AwaitingFirstSnapshot;
        public int Requests, Coalesced, Completed, WrongOwner, WrongEpoch;
        public double StartedAt, LastDuration, LastSnapshotAt, MaximumSnapshotGap;
    }
}
