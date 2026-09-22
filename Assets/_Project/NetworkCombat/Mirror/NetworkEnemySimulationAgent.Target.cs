using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationAgent
    {
        [SyncVar(hook = nameof(HandleTargetChanged))] private EnemyTargetState targetState;
        private Transform decoyTargetAnchor;
        public EnemyTargetState TargetState => targetState;
        public bool HasAllureDecoy => targetState.HasDecoy;
        public bool AllureHandoffPending => targetState.NeedsHandoff(assignment);
        public Transform ActualTarget => resolvedTarget;

        [Server]
        public void SetServerTarget(EnemyTargetState value)
        {
            targetState = value;
            ApplyTargetIntent();
        }

        private void HandleTargetChanged(EnemyTargetState previous, EnemyTargetState current)
        {
            ApplyTargetIntent();
        }

        private Transform ResolveSimulationTarget()
        {
            if (!targetState.HasDecoy)
            {
                ReleaseDecoyTargetAnchor();
                return ResolvePlayerTarget(assignment.AggroTargetPlayerId);
            }
            if (decoyTargetAnchor == null)
            {
                var anchor = new GameObject("Allure target " + netId) { hideFlags = HideFlags.HideAndDontSave };
                decoyTargetAnchor = anchor.transform;
            }
            decoyTargetAnchor.position = targetState.DecoyPosition;
            return decoyTargetAnchor;
        }

        private void ApplyTargetIntent()
        {
            if (targetState.Revision != 0) assignment.AggroTargetPlayerId = targetState.AggroPlayerId;
            resolvedTarget = ResolveSimulationTarget();
            // Do not reapply the simulation role or restore an action: a new target can
            // arrive during warning, dash, knockback, or the first snapshot of a handoff.
            if (authority != null)
                authority.ApplyRole(authority.Role, assignment.SimulationOwnerPlayerId,
                    assignment.AggroTargetPlayerId, assignment.Epoch);
            BindSimulationTarget();
        }

        private void BindSimulationTarget()
        {
            if (enemyController != null)
            {
                enemyController.Target = resolvedTarget;
                if (enemyController.attackScript != null) enemyController.attackScript.Target = resolvedTarget;
            }
            if (localChase != null && resolvedTarget != null) localChase.Initialize(resolvedTarget);
        }

        private void ReleaseDecoyTargetAnchor()
        {
            if (decoyTargetAnchor != null) Destroy(decoyTargetAnchor.gameObject);
            decoyTargetAnchor = null;
        }
    }
}
