using System;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Interactions;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
    public partial class EnemyAttackExplosion
    {
        private ulong localExplosionAction;
        private bool hasLocalExplosionCenter;
        private Vector2 localExplosionCenter;
        public Vector2 LocalExplosionCenter => localExplosionCenter;
        public bool HasLocalExplosionCenter => hasLocalExplosionCenter;
        public override bool SupportsSharedTimeline => true;
        public override EnemyAttackPrefab LocalAttackInstance => explosion;
        public override PlayerDamageInteraction LocalDamageInteraction => explosion != null ? explosion.damageInteraction : null;

        public override void PrepareTimeline(ref EnemyActionState state)
        {
            triggered = pending = false; center = default;
            state.Explosion = true; state.ExplosionTriggered = state.SelfDestructPending = false;
        }

        public override void ApplySimulationFrame(EnemyActionState state, double now)
        {
            if (state.Phase == EnemyAttackPresentationPhase.Cancelled || !state.Explosion)
            { triggered = pending = false; ReleaseSimulationMotion(); return; }
            if (state.Phase == EnemyAttackPresentationPhase.Warning)
            {
                triggered = pending = false;
                controller.stats.Speed = chaseSpeed; controller.ActivateColliders(true);
                controller.Movement.FreezeRigidbody(false);
                return;
            }
            if (!triggered)
            {
                center = state.ExplosionTriggered ? state.ExplosionPosition : (Vector2)explosionPosition.position;
                triggered = pending = true;
            }
            controller.stats.Speed = controller.stats.BaseSpeed;
            controller.ActivateColliders(false); controller.Movement.FreezeRigidbody(true);
        }

        public override void ReleaseSimulationMotion()
        {
            if (controller == null) return;
            controller.stats.Speed = controller.stats.BaseSpeed;
            controller.Movement?.FreezeRigidbody(false);
        }

        public override void ApplyLocalFrame(EnemyActionState state, double now, bool changed)
        {
            if (state.ActionId != localExplosionAction)
            {
                ReleaseLocalFrame(); localExplosionAction = state.ActionId;
                hasLocalExplosionCenter = false;
            }
            if (state.ActionId == 0 || state.Phase == EnemyAttackPresentationPhase.Cancelled ||
                state.Phase == EnemyAttackPresentationPhase.Inactive)
            { ReleaseLocalFrame(); return; }
            if (state.Phase == EnemyAttackPresentationPhase.Warning)
            {
                controller.ActivateColliders(true);
                if (warning == null) SpawnWarning((float)Math.Max(0, now - state.WarningStartedAt));
                warning?.SampleTimeline((float)(state.WarningUntil - state.WarningStartedAt),
                    (float)(state.ActiveUntil - state.WarningUntil), (float)(now - state.WarningStartedAt), true, changed);
                return;
            }
            ReturnWarning();
            if (!hasLocalExplosionCenter)
            {
                localExplosionCenter = explosionPosition.position;
                hasLocalExplosionCenter = true;
            }
            // Every peer fixes its own visible center once. Assignment changes never overwrite it.
            bool active = state.Phase == EnemyAttackPresentationPhase.Active && now >= state.WarningUntil && now < state.ActiveUntil;
            controller.ActivateColliders(false);
            if (explosion == null && now < state.RecoveryUntil)
            {
                explosionPool = PoolManager.Instance.GetOrCreatePooler(_attackVFXPrefab);
                explosion = explosionPool.GetOrCreate(transform, true);
                explosion.transform.position = localExplosionCenter;
                explosion.SetStats(controller.stats);
                explosion.TriggerVisual();
            }
            if (explosion == null) return;
            explosion.transform.position = localExplosionCenter;
            explosion.SetStats(controller.stats);
            explosion.SampleCombatTimeline((float)Math.Max(0, now - state.WarningUntil),
                (float)(state.ActiveUntil - state.WarningUntil));
            explosion.SetDamageEnabled(active);
        }

        public override void ReleaseLocalFrame()
        {
            ReturnWarning();
            explosion?.damageInteraction?.ConfigureAttackWindow(null);
            ReleaseExplosion();
        }

        public override void CancelLocalFrame()
        {
            ReleaseLocalFrame();
            if (controller != null && controller.IsAlive && !controller.DeathRequested)
                controller.ActivateColliders(true);
        }
    }
}
