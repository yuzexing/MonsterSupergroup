using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
    public partial class EnemyAttackDash
    {
        public override bool SupportsSharedTimeline => GetType() == typeof(EnemyAttackDash);
        public override AstralShift.HellMaiden.Interactions.PlayerDamageInteraction LocalDamageInteraction => damageInteraction;
        public override bool LocalDamageEnabled => attackCollider != null && attackCollider.enabled && damageInteraction != null && damageInteraction.enabled;

        public override void PrepareTimeline(ref EnemyActionState state)
        {
            BindCurrentStats();
            startPoint = rb.position; lastPosition = startPoint;
            _direction = state.Facing.normalized * distance;
            endPoint = startPoint + _direction; attackStartPosition = startPoint; hasDashState = true; _returning = false;
            if (controller.direction == AstralShift.HellMaiden.Common.Direction.Right) controller.direction = AstralShift.HellMaiden.Common.Direction.Left;
            else if (controller.direction == AstralShift.HellMaiden.Common.Direction.Left) controller.direction = AstralShift.HellMaiden.Common.Direction.Right;
            CaptureDashState(ref state);
        }

        public override void ApplySimulationFrame(EnemyActionState state, double now)
        {
            BindCurrentStats();
            if (state.Phase != EnemyAttackPresentationPhase.Active || now >= state.ActiveUntil)
            { EndDashMotion(); return; }
            BeginDashMotion();
            float progress = Mathf.Clamp01((float)((now - state.WarningUntil) / (state.ActiveUntil - state.WarningUntil)));
            Vector2 next = Vector2.Lerp(state.DashStart, state.DashEnd, movementCurve.Evaluate(progress));
            if (Time.deltaTime > 0) { rb.linearVelocity = (next - lastPosition) / Time.deltaTime; lastPosition = next; }
        }

        public override void ReleaseSimulationMotion() => EndDashMotion();
        public override void RestoreSimulationMotion(EnemyActionState state, double now)
        {
            BindCurrentStats();
            if (state.Phase == EnemyAttackPresentationPhase.Active && now < state.ActiveUntil) BeginDashMotion();
            else EndDashMotion();
        }
        public override void ApplyLocalFrame(EnemyActionState state, double now, bool changed)
        {
            base.ApplyLocalFrame(state, now, changed);
            bool active = state.Phase == EnemyAttackPresentationPhase.Active && now >= state.WarningUntil && now < state.ActiveUntil;
            // No motion from this path, even on the machine currently simulating the enemy.
            SetDashDamageEnabled(active);
        }
        public override void ReleaseLocalFrame()
        { SetDashDamageEnabled(false); damageInteraction?.ConfigureAttackWindow(null); base.ReleaseLocalFrame(); }
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
            // Reapplying body state every render frame resets interpolated physics poses
            // and loses dash travel. Capture and apply it only when motion begins.
            if (changedMotion) return;
            previousConstraints = rb.constraints; previousSimulated = rb.simulated;
            previousExclusions = collider.excludeLayers; changedMotion = true;
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
