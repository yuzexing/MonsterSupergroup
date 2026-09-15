using System;
using UnityEngine;
using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.AI.Enemy
{
    public partial class EnemyController
    {
        private Func<double> simulationClock;
        private uint simulationEpoch, simulationActionSequence;
        private EnemyActionState simulationAction;

        public void ConfigureSimulationClock(Func<double> clock, uint epoch)
        {
            simulationClock = clock;
            if (simulationEpoch != epoch) { simulationEpoch = epoch; simulationActionSequence = 0; }
        }

        private Vector2 GetSimulationAttackTargetPosition() => simulationClock != null &&
            (simulationAction.Phase == EnemyAttackPresentationPhase.Warning || simulationAction.Phase == EnemyAttackPresentationPhase.Active)
                ? simulationAction.TargetPosition : GetTargetPosition;

        private void RecordSimulationActionPhase(EnemyAttackPresentationPhase phase)
        {
            if (simulationClock == null) return;
            double now = simulationClock();
            if (phase == EnemyAttackPresentationPhase.Warning)
            {
                simulationActionSequence++;
                simulationAction = new EnemyActionState
                {
                    ActionId = ((ulong)simulationEpoch << 32) | simulationActionSequence,
                    WarningStartedAt = now, WarningUntil = now + GetAttackPresentationPhaseDuration(phase),
                    Facing = FacingDirection, TargetPosition = GetTargetPosition
                };
                simulationAction.ActiveUntil = simulationAction.WarningUntil + GetAttackPresentationPhaseDuration(EnemyAttackPresentationPhase.Active);
                simulationAction.RecoveryUntil = simulationAction.ActiveUntil + GetAttackPresentationPhaseDuration(EnemyAttackPresentationPhase.Recovery);
                simulationAction.NextAttackAt = simulationAction.RecoveryUntil + attackCooldown;
            }
            simulationAction.Phase = phase;
            if (phase == EnemyAttackPresentationPhase.Active || phase == EnemyAttackPresentationPhase.Recovery)
                attackScript?.RestoreSimulationTimers((float)(now - simulationAction.WarningStartedAt),
                    (float)(now - simulationAction.WarningUntil), (float)(now - simulationAction.ActiveUntil));
            if (phase == EnemyAttackPresentationPhase.Cancelled)
                simulationAction.NextAttackAt = now + Math.Max(0, attackCooldown - (Time.time - lastAttackTime));
        }

        public EnemyActionState CaptureSimulationAction(double now)
        {
            var state = simulationAction;
            if (attackScript is EnemyAttackDash dash) dash.CaptureDashState(ref state);
            if (attackScript is EnemyProjectileAttack projectile && projectile.NetworkExecution != null)
            {
                state.ProjectileDirection = projectile.LockedDirection;
                state.ProjectileEmitted = projectile.ProjectileEmitted;
            }
            if (state.Phase == EnemyAttackPresentationPhase.Inactive)
                state.NextAttackAt = float.IsNegativeInfinity(lastAttackTime) ? 0 : now + Math.Max(0, attackCooldown - (Time.time - lastAttackTime));
            return state;
        }

        public bool TryReadSimulationAction(out EnemyActionState action, out double now)
        {
            action = simulationAction; now = simulationClock != null ? simulationClock() : Time.timeAsDouble;
            return simulationClock != null;
        }

        public void SuspendSimulationExecution()
        {
            if (attackScript is EnemyAttackMelee melee) melee.SuspendSimulation();
            if (attackScript is EnemyProjectileAttack projectile) projectile.NetworkExecution?.CancelCharge();
            // Do not execute Knockback.onExit or a deferred melee collision when giving up a lease.
            if (_networkKnockbackMovement != null) _networkKnockbackMovement.CancelKnockback();
            if (_networkMovementOnlyKnockback)
            {
                // Restore dormant navigation flags before discarding this local impulse.
                // Otherwise A→B→A after it expires can leave A permanently stopped.
                if (_networkRestoreDefaultMovement) defaultMovement?.ResumeMovement();
                if (_networkRestorePathMovement && usesPathfinding) aILerpMovement?.ResumeMovement();
                _canBeStuck = _networkRestoreCanBeStuck;
            }
            _networkKnockbackMovement = null;
            _networkMovementOnlyKnockback = false;
        }

        public void RestoreSimulationAction(EnemyActionState state, double now)
        {
            simulationAction = state;
            if (_stateMachine == null || attackScript == null) return;
            var phase = state.PhaseAt(now);
            simulationAction.Phase = phase;
            CurrentAttackPresentationPhase = phase;
            lastAttackTime = (float)(Time.time + Math.Max(0, state.NextAttackAt - now) - attackCooldown);
            var fsmState = phase == EnemyAttackPresentationPhase.Warning ? Warning :
                phase == EnemyAttackPresentationPhase.Active ? Attacking :
                phase == EnemyAttackPresentationPhase.Recovery ? Recovery : Moving;
            _stateMachine.RestoreStateNoCallbacks(fsmState);
            previousFacingDirection = state.Facing.sqrMagnitude > .0001f ? state.Facing : Vector2.right;
            Movement.SetFacingDirection(previousFacingDirection);
            bool attacking = fsmState != Moving;
            _canRubberband = !attacking;
            if (attacking && stopForAttack) { SetDefaultMovement(); DisableMovement(); }
            else { EnableMovement(); RefreshMovementMethod(); }
            Movement.FreezeRigidbody(attacking && stopForAttack);
            attackScript.RestoreSimulationTimers((float)(now - state.WarningStartedAt),
                (float)(now - state.WarningUntil), (float)(now - state.ActiveUntil));
            if (attackScript is EnemyAttackDash dash) dash.RestoreDashState(state);
            if (attackScript is EnemyAttackMelee melee)
                melee.RestoreSimulation(phase, previousFacingDirection, (float)Math.Max(0, state.EndAt(phase) - now));
            if (attackScript is EnemyProjectileAttack projectile) projectile.RestoreProjectileSimulation(state, phase);
            ApplyReplicatedAttackPresentation(phase, previousFacingDirection, Math.Max(0, now - state.StartAt(phase)));
        }

        public EnemyKnockbackMotionState CaptureSimulationKnockback()
        {
            if (_networkKnockbackMovement == null) return default;
            var state = _networkKnockbackMovement.CaptureSimulationKnockback();
            state.RestoreDefaultMovement = _networkMovementOnlyKnockback ? _networkRestoreDefaultMovement : true;
            state.RestorePathMovement = _networkMovementOnlyKnockback ? _networkRestorePathMovement : usesPathfinding;
            state.RestoreCanBeStuck = _networkMovementOnlyKnockback ? _networkRestoreCanBeStuck : _canBeStuck;
            return state;
        }

        public bool RestoreSimulationKnockback(EnemyKnockbackMotionState state, KnockbackSettings preset, float elapsedSinceSample)
        {
            if (!state.Active || Movement == null) return false;
            state.Elapsed += Mathf.Max(0, elapsedSinceSample);
            if (state.Elapsed >= state.Duration + state.StaggerDuration)
            {
                if (state.RestoreDefaultMovement) defaultMovement?.ResumeMovement(); else defaultMovement?.StopMovement();
                if (usesPathfinding)
                {
                    if (state.RestorePathMovement) aILerpMovement?.ResumeMovement(); else aILerpMovement?.StopMovement();
                }
                _canBeStuck = state.RestoreCanBeStuck;
                return false;
            }
            _networkKnockbackMovement = Movement;
            _networkMovementOnlyKnockback = _stateMachine == null;
            _networkRestoreDefaultMovement = state.RestoreDefaultMovement;
            _networkRestorePathMovement = state.RestorePathMovement;
            _networkRestoreCanBeStuck = state.RestoreCanBeStuck;
            if (_stateMachine != null) _stateMachine.RestoreStateNoCallbacks(Knockback);
            _canBeStuck = false;
            defaultMovement?.StopMovement();
            if (usesPathfinding) aILerpMovement?.StopMovement();
            Movement.FreezeRigidbody(false);
            Movement.RestoreSimulationKnockback(state, preset, () =>
            {
                if (_networkMovementOnlyKnockback) CompleteNetworkMovementKnockback();
                else
                {
                    _networkKnockbackMovement = null;
                    _canBeStuck = state.RestoreCanBeStuck;
                    if (IsAlive) TransitionToMoving();
                }
            });
            return true;
        }
    }
}
