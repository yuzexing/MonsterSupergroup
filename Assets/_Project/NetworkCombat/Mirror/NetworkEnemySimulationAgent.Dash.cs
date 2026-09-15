using System;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationAgent
    {
        internal bool ValidateDashAction(EnemyActionState action)
        {
            var dash = enemyController != null ? enemyController.attackScript as EnemyAttackDash : null;
            if (dash == null) return !action.Dash;
            if (action.ActionId == 0) return !action.Dash;
            if (!action.Dash) return false;
            if (Mathf.Abs(Vector2.Distance(action.DashStart, action.DashEnd) - dash.distance) > .001f) return false;
            if (Math.Abs(action.WarningUntil - action.WarningStartedAt - dash.WarningTime) > .001 ||
                Math.Abs(action.ActiveUntil - action.WarningUntil - dash.AttackTime) > .001 ||
                Math.Abs(action.RecoveryUntil - action.ActiveUntil - dash.RecoveryTime) > .001) return false;
            var delta = action.DashEnd - action.DashStart;
            float u = Vector2.Dot(action.DashLastPosition - action.DashStart, delta) / Mathf.Max(.0001f, delta.sqrMagnitude);
            return u >= -.001f && u <= 1.001f &&
                Vector2.Distance(action.DashLastPosition, action.DashStart + delta * u) < .01f;
        }
    }
}
