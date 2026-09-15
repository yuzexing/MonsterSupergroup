using System;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
    public partial class EnemyController
    {
        private void BeginSequenceAction(double now, SequenceEnemyAttack sequence)
        {
            var animator = (MultipleAttackAnimator)enemyAnimator;
            var warnings = animator.SequenceWarnings;
            var actives = animator.SequenceActives;
            simulationActionSequence++;
            simulationAction = new EnemyActionState
            {
                ActionId = ((ulong)simulationEpoch << 32) | simulationActionSequence,
                Sequence = true, ComboStartedAt = now, SequenceWarnings = warnings, SequenceActives = actives,
                Phase = EnemyAttackPresentationPhase.Warning, StrikeIndex = 0, PoseStrikeIndex = 0,
                LockedStrikeMask = 1, Facing = FacingDirection, TargetPosition = GetTargetPosition,
                RecoveryUntil = now + EnemySequenceTimeline.Duration(warnings, actives) + sequence.RecoveryTime
            };
            simulationAction.NextAttackAt = simulationAction.RecoveryUntil + attackCooldown;
            simulationAction = EnemySequenceTimeline.Resolve(simulationAction, now);
        }

        public bool TickSequenceAction()
        {
            if (simulationClock == null || !simulationAction.Sequence || attackScript is not SequenceEnemyAttack) return false;
            double now = simulationClock();
            var next = EnemySequenceTimeline.Resolve(simulationAction, now);
            if (next.Phase == EnemyAttackPresentationPhase.Cancelled) return true;
            if (next.StrikeIndex == simulationAction.StrikeIndex && next.Phase == simulationAction.Phase) return true;
            // Lock only at a live Warning boundary. A missed Warning cannot invent a past pose.
            if (next.StrikeIndex != simulationAction.StrikeIndex && next.Phase == EnemyAttackPresentationPhase.Warning)
            {
                next.Facing = (GetTargetPosition - (Vector2)transform.position).normalized;
                if (next.Facing.sqrMagnitude < .0001f) next.Facing = FacingDirection;
                next.TargetPosition = GetTargetPosition; next.PoseStrikeIndex = next.StrikeIndex;
                next.LockedStrikeMask |= (byte)(1 << next.StrikeIndex);
            }
            if (next.Phase == EnemyAttackPresentationPhase.Active && EnemySequenceTimeline.HasPose(next))
                next.ExecutedStrikeMask |= (byte)(1 << next.StrikeIndex);
            ApplySequenceFrame(next, now, true);
            PublishAttackPresentationPhase(next.Phase);
            return true;
        }

        private void ApplySequenceFrame(EnemyActionState state, double now, bool settlePrevious)
        {
            state = EnemySequenceTimeline.Resolve(state, now);
            var sequence = (SequenceEnemyAttack)attackScript;
            // Attribute a valid final-frame contact to the window that generated it.
            if(settlePrevious && IsAlive && !DeathRequested)sequence.SimulationAttackInstance?.damageInteraction?.SettlePendingCollisions();
            simulationAction = state; CurrentAttackPresentationPhase = state.Phase;
            bool active = state.Phase == EnemyAttackPresentationPhase.Warning || state.Phase == EnemyAttackPresentationPhase.Active ||
                state.Phase == EnemyAttackPresentationPhase.Recovery;
            _stateMachine.RestoreStateNoCallbacks(state.Phase == EnemyAttackPresentationPhase.Warning ? Warning :
                state.Phase == EnemyAttackPresentationPhase.Active ? Attacking : state.Phase == EnemyAttackPresentationPhase.Recovery ? Recovery : Moving);
            previousFacingDirection = state.Facing.sqrMagnitude > .0001f ? state.Facing : Vector2.right;
            Movement.SetFacingDirection(previousFacingDirection); _canRubberband = !active;
            if (active) { SetDefaultMovement(); DisableMovement(); }
            else { EnableMovement(); RefreshMovementMethod(); }
            Movement.FreezeRigidbody(active);
            lastAttackTime = (float)(Time.time + Math.Max(0, state.NextAttackAt-now) - attackCooldown);
            sequence.RestoreSequence(state, now, false);
            ApplyReplicatedAttackPresentation(state.Phase, state.Facing, Math.Max(0, now-state.StartAt(state.Phase)));
            if (!active) sequence.currentAttackCount = 0;
        }
    }
}
