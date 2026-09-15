using System;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationAgent
    {
        private bool explosionDisposalCommitted;
        internal bool ValidateExplosionAction(EnemyActionState action, Vector2 position)
        {
            var attack = enemyController != null ? enemyController.attackScript as EnemyAttackExplosion : null;
            if (attack == null || action.ActionId == 0) return !action.Explosion && !action.ExplosionTriggered && !action.SelfDestructPending;
            if (!action.Explosion || action.Dash || action.ProjectileEmitted) return false;
            if (action.Phase == EnemyAttackPresentationPhase.Warning && (action.ExplosionTriggered || action.SelfDestructPending)) return false;
            if ((action.Phase == EnemyAttackPresentationPhase.Active || action.Phase == EnemyAttackPresentationPhase.Recovery) &&
                (!action.ExplosionTriggered || !action.SelfDestructPending)) return false;
            if (Math.Abs(action.WarningUntil-action.WarningStartedAt-attack.WarningTime)>.001 ||
                Math.Abs(action.ActiveUntil-action.WarningUntil-attack.AttackTime)>.001 ||
                Math.Abs(action.RecoveryUntil-action.ActiveUntil-attack.RecoveryTime)>.001) return false;
            return !action.ExplosionTriggered || Vector2.Distance(action.ExplosionPosition, position+attack.ExplosionOffset)<.25f;
        }
        private void TickExplosionLifecycle()
        {
            if (!isServer || explosionDisposalCommitted || !productEnemyInitialized || !IsCanonicalAlive ||
                enemyController.attackScript is not EnemyAttackExplosion attack) return;
            EnemyActionState action;
            if (authority.RunsCombatDecisions) action = enemyController.CaptureSimulationAction(EnemySimulationClock.CombatNow);
            else
            {
                var registry = NetworkEnemySimulationWorld.Instance?.Registry;
                if (registry == null || !registry.TryGetLatestSnapshot(netId, out var latest) || latest.AssignmentEpoch != assignment.Epoch) return;
                action = latest.Runtime.Action;
            }
            if (!action.Explosion || !action.ExplosionTriggered || !action.SelfDestructPending ||
                action.Phase == EnemyAttackPresentationPhase.Cancelled || EnemySimulationClock.CombatNow < action.RecoveryUntil) return;
            explosionDisposalCommitted = true;
            Debug.Log(FormattableString.Invariant($"[LostSoulDispose] id={netId} epoch={assignment.Epoch} action={action.ActionId} combat={EnemySimulationClock.CombatNow:R} deadline={action.RecoveryUntil:R} noXp=true"));
            FindFirstObjectByType<NetworkGameplayEnemySpawner>()?.RecordReferenceSelfDestruct(this);
            attack.CancelAttack();
            // The existing network destruction unregisters ledger/simulation state.
            // No ConfirmedKill is emitted, so XP/loot/player credit stay untouched.
            NetworkServer.Destroy(gameObject);
        }
    }
}
