using System;
using AstralShift.HellMaiden.AI;

namespace MonsterSupergroup.NetworkCombat
{
    public enum EnemyTargetChangeReason : byte { Spawn, TargetDowned, TargetDisconnected, TargetUnavailable, Forced, Resume, Timeout, SimulatorReady }
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
            EnemyKnockbackSettings.Finite(Action.NextAttackAt) && Finite(Action.Facing) && Finite(Action.TargetPosition) && Finite(Action.ProjectileDirection) && ValidReceipts &&
            (!Knockback.Active || (KnockbackSettings.IsValid && Finite(Knockback.Start) && Finite(Knockback.End) &&
            Finite(Knockback.LastPosition) && EnemyKnockbackSettings.Finite(Knockback.Elapsed) && Knockback.Elapsed >= 0 &&
            EnemyKnockbackSettings.Finite(Knockback.Duration) && Knockback.Duration > 0 &&
            EnemyKnockbackSettings.Finite(Knockback.StaggerDuration) && Knockback.StaggerDuration >= 0));
        private static bool Finite(UnityEngine.Vector2 value) => EnemyKnockbackSettings.Finite(value.x) && EnemyKnockbackSettings.Finite(value.y);
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
