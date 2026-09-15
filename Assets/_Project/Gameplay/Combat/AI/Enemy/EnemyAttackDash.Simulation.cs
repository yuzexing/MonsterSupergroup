using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
    public partial class EnemyAttackDash
    {
        private bool hasDashState, changedMotion;
        private RigidbodyConstraints2D previousConstraints;
        private bool previousSimulated;
        private LayerMask previousExclusions;

        public void BindCurrentStats()
        {
            if (controller == null) controller = GetComponent<EnemyController>();
            if (controller == null) return;
            if (rb == null) rb = controller.rigidBody;
            collider = controller.collider;
            if (damageInteraction != null)
            {
                damageInteraction.enemyStats = controller.stats;
                damageInteraction.FlushPendingCollisionsOnDisable = false;
            }
        }

        // Both execution paths use this body's existing local-player damage interaction.
        // Only the simulator calls BeginDashMotion; a replica never performs dash movement.
        public void SetDashDamageEnabled(bool enabled, bool settle = false)
        {
            BindCurrentStats();
            if (!enabled)
            {
                if (settle) damageInteraction?.SettlePendingCollisions();
                else damageInteraction?.DiscardPendingCollisions();
            }
            if (attackCollider != null) attackCollider.enabled = enabled;
            if (damageInteraction != null) damageInteraction.enabled = enabled;
        }

        private void BeginDashMotion()
        {
            if (!changedMotion)
            {
                previousConstraints = rb.constraints; previousSimulated = rb.simulated;
                previousExclusions = collider.excludeLayers; changedMotion = true;
            }
            rb.constraints = RigidbodyConstraints2D.FreezeRotation; rb.simulated = true;
            collider.excludeLayers = dashExclusionLayerMask;
        }

        private void EndDashMotion()
        {
            if (!changedMotion) return;
            if (rb != null) { rb.linearVelocity = Vector2.zero; rb.constraints = previousConstraints; rb.simulated = previousSimulated; }
            if (collider != null) collider.excludeLayers = previousExclusions;
            changedMotion = false;
        }

        public override void SuspendSimulation()
        {
            SetDashDamageEnabled(false); EndDashMotion(); base.SuspendSimulation();
        }

        public void CaptureDashState(ref EnemyActionState action)
        {
            action.Dash = hasDashState && action.ActionId != 0;
            if (!action.Dash) return;
            action.DashStart = startPoint; action.DashEnd = endPoint;
            action.DashLastPosition = lastPosition; action.DashWarningOrigin = attackStartPosition;
        }

        public void RestoreDashState(EnemyActionState action)
        {
            hasDashState = action.Dash;
            startPoint = action.DashStart; endPoint = action.DashEnd;
            lastPosition = action.DashLastPosition; attackStartPosition = action.DashWarningOrigin;
            _direction = endPoint - startPoint; _returning = false;
        }

        public override void RestoreSimulation(EnemyAttackPresentationPhase phase, Vector2 facing, float remaining)
        {
            base.RestoreSimulation(phase, facing, remaining);
            BindCurrentStats();
            if (!hasDashState || remaining <= 0) return;
            if (_attack != null) _attack.transform.position = attackStartPosition;
            if (phase == EnemyAttackPresentationPhase.Active) { BeginDashMotion(); SetDashDamageEnabled(true); }
        }
    }
}
