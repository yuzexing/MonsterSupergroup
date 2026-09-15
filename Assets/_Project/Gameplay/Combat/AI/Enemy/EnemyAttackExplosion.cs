using AstralShift.Pooling;
using AstralShift.HellMaiden.Combat;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
    public class EnemyAttackExplosion : EnemyAttack
    {
        [SerializeField] private EnemyAttackWarning _attackWarningPrefab;
        [SerializeField] private EnemyExplosionAttackVFX _attackVFXPrefab;
        [SerializeField] private Transform explosionPosition;
        [SerializeField] private float chaseSpeed = 6.2f;
        private GenericPooler<EnemyAttackWarning> warningPool;
        private GenericPooler<EnemyExplosionAttackVFX> explosionPool;
        private EnemyAttackWarning warning;
        private EnemyExplosionAttackVFX explosion;
        private bool triggered, pending;
        private Vector2 center;
        private ulong presentationAction;
        private EnemyAttackPresentationPhase presentationPhase;
        private bool NetworkManaged => controller != null && controller.GetComponent<EnemySimulationAuthority>()?.IsNetworkManaged == true;
        public EnemyAttackWarning WarningInstance => warning;
        public EnemyExplosionAttackVFX ExplosionInstance => explosion;
        public float ChaseSpeed => chaseSpeed;
        public Vector2 ExplosionOffset => explosionPosition != null ? explosionPosition.position - transform.position : Vector3.zero;

        public override void AttackWarningEnter()
        {
            ReleaseExplosion(); ReturnWarning();
            triggered = pending = false;
            _warningStartTime = Time.time;
            controller.stats.Speed = chaseSpeed;
            controller.Movement.FreezeRigidbody(false);
            SpawnWarning(0);
        }
        public override void AttackWarningTick()
        {
            enemyAnimator.AttackWarning(controller.FacingDirection.x, controller.FacingDirection.y);
            base.AttackWarningTick();
        }
        public override void AttackWarningExit() { base.AttackWarningExit(); ReturnWarning(); }
        public override void AttackEnter()
        {
            base.AttackEnter();
            center = explosionPosition.position;
            triggered = pending = true;
            controller.stats.Speed = controller.stats.BaseSpeed;
            controller.ActivateColliders(false);
            controller.Movement.FreezeRigidbody(true);
            AcquireExplosion(0, true);
        }
        public override void RecoveryEnter()
        {
            base.RecoveryEnter(); CloseExpiredWindow();
        }
        public override void RecoveryTick()
        {
            if (NetworkManaged) return; // Server disposal uses the accepted action deadline.
            if (Time.time - _recoveryStartTime <= RecoveryTime) return;
            controller.Kill(true, false); ReleaseExplosion();
        }
        public override void CancelAttack()
        {
            SuspendExplosion(); triggered = pending = false;
        }
        public void SuspendExplosion()
        {
            ReturnWarning(); ReleaseExplosion();
            if (controller != null)
            {
                controller.stats.Speed = controller.stats.BaseSpeed;
                controller.Movement?.FreezeRigidbody(false);
            }
            presentationAction = 0;
        }
        private void OnDisable() => SuspendExplosion();
        public void CaptureExplosion(ref EnemyActionState state)
        {
            state.Explosion = state.ActionId != 0;
            state.ExplosionTriggered = triggered;
            state.SelfDestructPending = pending;
            state.ExplosionPosition = center;
        }
        public void RestoreExplosion(EnemyActionState state, EnemyAttackPresentationPhase phase, double now, bool simulator)
        {
            triggered = state.ExplosionTriggered; pending = state.SelfDestructPending; center = state.ExplosionPosition;
            if (state.ActionId == 0 || !state.Explosion || phase == EnemyAttackPresentationPhase.Cancelled)
            { CancelAttack(); return; }
            if (simulator && state.Phase == EnemyAttackPresentationPhase.Warning && phase != EnemyAttackPresentationPhase.Warning)
            {
                // The lease can arrive after Warning ended. Commit its one-shot at
                // the restored position, with only the original remaining window.
                // A Replica must wait for the accepted simulator state instead.
                triggered = pending = true; center = explosionPosition.position;
                state.ExplosionTriggered = state.SelfDestructPending = true;
                state.ExplosionPosition = center;
            }
            bool changed = presentationAction != state.ActionId || presentationPhase != phase;
            presentationAction = state.ActionId; presentationPhase = phase;
            if (phase == EnemyAttackPresentationPhase.Warning)
            {
                controller.stats.Speed = chaseSpeed;
                if (simulator) { controller.ActivateColliders(true); controller.Movement.FreezeRigidbody(false); }
                if (changed || warning == null) SpawnWarning((float)System.Math.Max(0, now-state.WarningStartedAt));
                return;
            }
            ReturnWarning();
            controller.stats.Speed = controller.stats.BaseSpeed;
            if (state.SelfDestructPending)
            {
                controller.ActivateColliders(false);
                if (simulator) controller.Movement.FreezeRigidbody(true);
            }
            bool active = phase == EnemyAttackPresentationPhase.Active && now < state.ActiveUntil;
            if (state.ExplosionTriggered && now < state.RecoveryUntil)
            {
                if (explosion == null || changed) AcquireExplosion((float)System.Math.Max(0, now-state.WarningUntil), active);
                else if (!active) explosion.Stop();
            }
            else ReleaseExplosion();
        }
        private void SpawnWarning(float elapsed)
        {
            if (warning == null)
            {
                warningPool = PoolManager.Instance.GetOrCreatePooler(_attackWarningPrefab);
                warning = warningPool.GetOrCreate(transform, true);
            }
            warning.transform.localPosition = Vector3.zero;
            warning.SetWarningTime(WarningTime, AttackTime); warning.Show();
            var animancer = warning.GetComponent<Animancer.AnimancerComponent>();
            if (animancer != null && animancer.Layers[0].CurrentState != null)
                animancer.Layers[0].CurrentState.Time = elapsed / WarningTime;
        }
        private void ReturnWarning()
        {
            if (warning == null) return;
            warning.Hide(); var released = warning; warning = null;
            if (gameObject.activeInHierarchy) warningPool.Return(released);
            else { released.gameObject.SetActive(false); ReturnWarningDeferred(released); }
        }
        private async void ReturnWarningDeferred(EnemyAttackWarning released)
        {
            var owner = PoolManager.Instance;
            await Cysharp.Threading.Tasks.UniTask.NextFrame();
            if (released == null) return;
            if (owner != null && owner == PoolManager.Instance) warningPool.Return(released);
            else Destroy(released.gameObject);
        }
        private void AcquireExplosion(float elapsed, bool active)
        {
            if (explosion == null)
            {
                explosionPool = PoolManager.Instance.GetOrCreatePooler(_attackVFXPrefab);
                explosion = explosionPool.GetOrCreate(transform, true);
            }
            explosion.transform.position = center;
            explosion.SetStats(controller.stats); explosion.Trigger(null);
            if (elapsed > 0) explosion.particleSystem.Simulate(elapsed, true, true, true);
            if (active) explosion.particleSystem.Play(true); else explosion.Stop();
        }
        private void ReleaseExplosion()
        {
            if (explosion == null) return;
            explosion.damageInteraction?.DiscardPendingCollisions();
            explosion.Stop(); explosion.particleSystem.Clear(true);
            var released = explosion; explosion = null;
            if (gameObject.activeInHierarchy) explosionPool.Return(released);
            else { released.gameObject.SetActive(false); ReturnExplosionDeferred(released); }
        }
        private async void ReturnExplosionDeferred(EnemyExplosionAttackVFX released)
        {
            var owner = PoolManager.Instance;
            await Cysharp.Threading.Tasks.UniTask.NextFrame();
            if (released == null) return;
            if (owner != null && owner == PoolManager.Instance) explosionPool.Return(released);
            else Destroy(released.gameObject);
        }
        public void CloseExpiredWindow()
        {
            if (controller != null && controller.IsAlive && !controller.DeathRequested) explosion?.damageInteraction?.SettlePendingCollisions();
            else explosion?.damageInteraction?.DiscardPendingCollisions();
            explosion?.Stop();
        }
    }
}
