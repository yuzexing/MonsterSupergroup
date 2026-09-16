using System;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
    public partial class EnemyController
    {
        public bool UsesSharedAttackTimeline => simulationClock != null && attackScript != null && attackScript.SupportsSharedTimeline;

        private void BeginSharedAction(double now)
        {
            simulationActionSequence++;
            simulationAction = EnemyActionTimeline.Begin(((ulong)simulationEpoch << 32) | simulationActionSequence,
                now, attackScript.TimelineStrikes, attackScript.RecoveryTime, attackCooldown, FacingDirection, GetTargetPosition);
            attackScript.PrepareTimeline(ref simulationAction);
            ApplySharedSimulationFrame(simulationAction, now);
        }

        public bool TickSequenceAction() => UsesSharedAttackTimeline && TickSharedAction();

        public bool TickSharedAction()
        {
            if (!UsesSharedAttackTimeline || simulationAction.ActionId == 0) return false;
            double now = simulationClock();
            var next = EnemyActionTimeline.Resolve(simulationAction, now);
            if (next.Phase == EnemyAttackPresentationPhase.Cancelled) return true;
            bool changed = next.StrikeIndex != simulationAction.StrikeIndex || next.Phase != simulationAction.Phase;
            if (next.Sequence && next.StrikeIndex != simulationAction.StrikeIndex && next.Phase == EnemyAttackPresentationPhase.Warning)
            {
                next.Facing = (GetTargetPosition - (Vector2)transform.position).normalized;
                if (next.Facing.sqrMagnitude < .0001f) next.Facing = FacingDirection;
                next.TargetPosition = GetTargetPosition; next.PoseStrikeIndex = next.StrikeIndex;
                next.LockedStrikeMask |= (byte)(1 << next.StrikeIndex);
            }
            if (next.Sequence && next.Phase == EnemyAttackPresentationPhase.Active && EnemyActionTimeline.HasPose(next))
                next.ExecutedStrikeMask |= (byte)(1 << next.StrikeIndex);
            if (next.Explosion && next.Phase == EnemyAttackPresentationPhase.Warning) next.Facing = FacingDirection;
            if (changed) ApplySharedSimulationFrame(next, now);
            else { simulationAction = next; attackScript.ApplySimulationFrame(next, now); }
            if (changed) PublishAttackPresentationPhase(next.Phase);
            return true;
        }

        private void ApplySharedSimulationFrame(EnemyActionState state, double now, bool restoring = false)
        {
            state = EnemyActionTimeline.Resolve(state, now);
            simulationAction = state; CurrentAttackPresentationPhase = state.Phase;
            bool pendingDisposal = state.Explosion && state.ActionId != 0 && state.Phase == EnemyAttackPresentationPhase.Inactive;
            bool attacking = state.Phase == EnemyAttackPresentationPhase.Warning || state.Phase == EnemyAttackPresentationPhase.Active ||
                state.Phase == EnemyAttackPresentationPhase.Recovery || pendingDisposal;
            _stateMachine.RestoreStateNoCallbacks(state.Phase == EnemyAttackPresentationPhase.Warning ? Warning :
                state.Phase == EnemyAttackPresentationPhase.Active ? Attacking :
                state.Phase == EnemyAttackPresentationPhase.Recovery || pendingDisposal ? Recovery : Moving);
            previousFacingDirection = state.Facing.sqrMagnitude > .0001f ? state.Facing : Vector2.right;
            Movement.SetFacingDirection(previousFacingDirection); _canRubberband = !attacking;
            if (attacking && stopForAttack) { SetDefaultMovement(); DisableMovement(); }
            else { EnableMovement(); RefreshMovementMethod(); }
            Movement.FreezeRigidbody(attacking && stopForAttack);
            lastAttackTime = (float)(Time.time + Math.Max(0, state.NextAttackAt - now) - attackCooldown);
            if (attackScript is EnemyAttackDash dash) dash.RestoreDashState(state);
            if (restoring) attackScript.RestoreSimulationMotion(state, now);
            else attackScript.ApplySimulationFrame(state, now);
        }
    }
}
