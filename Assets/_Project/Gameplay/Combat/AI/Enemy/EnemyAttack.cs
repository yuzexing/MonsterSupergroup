using System;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public abstract class EnemyAttack : MonoBehaviour
	{
        public virtual bool SupportsSharedTimeline => false;
        private EnemyStrikeTiming[] timelineStrikes;
        public System.Collections.Generic.IReadOnlyList<EnemyStrikeTiming> TimelineStrikes => timelineStrikes ??= CreateTimelineStrikes();
        protected virtual EnemyStrikeTiming[] CreateTimelineStrikes() => new[] { new EnemyStrikeTiming(WarningTime, AttackTime) };
        public virtual void PrepareTimeline(ref EnemyActionState state) { }
        public virtual void CaptureSimulationMotion(ref EnemyActionState state) { }
        public virtual void ApplySimulationFrame(EnemyActionState state, double now) { }
        public virtual void RestoreSimulationMotion(EnemyActionState state, double now) => ApplySimulationFrame(state, now);
        public virtual void ReleaseSimulationMotion() { }
        public virtual void ApplyLocalFrame(EnemyActionState state, double now, bool changed) { }
        public virtual void ReleaseLocalFrame() { }
        // Accepted cancellation may restore temporary body state; normal completion/death may not.
        public virtual void CancelLocalFrame() => ReleaseLocalFrame();
        public virtual EnemyAttackPrefab LocalAttackInstance => null;
        public virtual AstralShift.HellMaiden.Interactions.PlayerDamageInteraction LocalDamageInteraction => null;
        public virtual bool LocalDamageEnabled => LocalDamageInteraction != null && LocalDamageInteraction.isActiveAndEnabled;
		[SerializeField]
		[Range(0f, 10f)]
		protected float warningTime;

		[SerializeField]
		[Range(0f, 10f)]
		protected float attackTime;

		[SerializeField]
		[Range(0f, 10f)]
		protected float recoveryTime;

		protected float _attackStartTime;

		protected float _warningStartTime;

		protected float _recoveryStartTime;

		protected GenericPooler<EnemyAttackPrefab> _attackPooler;

		public Action<float> OnWarningTick;

		public Action onAttackEnd;

		public Action onAttackWarningEnd;

		public Action onRecoveryEnd;

		public Action onAttackWarningExit;

		public float WarningTime => warningTime + enemyAnimator.AttackWarningTime;

		public float AttackTime => attackTime + enemyAnimator.AttackTime;

		public float RecoveryTime => recoveryTime + enemyAnimator.RecoveryTime;

		public EnemyController controller { get; set; }

		public EnemyAnimator enemyAnimator { get; set; }

		public Transform Target { get; set; }

		[HideInInspector]
		public virtual bool OverrideKnockback => false;

		public virtual void AttackWarningEnter()
		{
			controller.Movement.FreezeRigidbody(state: true);
			_warningStartTime = Time.time;
		}

		public virtual void AttackWarningTick()
		{
			float num = Time.time - _warningStartTime;
			OnWarningTick?.Invoke(num / WarningTime);
			if (num > WarningTime)
			{
				onAttackWarningEnd?.Invoke();
			}
		}

		public virtual void AttackWarningExit()
		{
			onAttackWarningExit?.Invoke();
		}

		public virtual void AttackEnter()
		{
			_attackStartTime = Time.time;
		}

		public virtual void AttackTick()
		{
			if (Time.time - _attackStartTime > AttackTime)
			{
				onAttackEnd?.Invoke();
			}
		}

		public virtual void AttackExit()
		{
		}

		public virtual void RecoveryEnter()
		{
			_recoveryStartTime = Time.time;
		}

		public virtual void RecoveryTick()
		{
			if (Time.time - _recoveryStartTime > RecoveryTime)
			{
				onRecoveryEnd?.Invoke();
			}
		}

		public virtual void RecoveryExit()
		{
		}

		public abstract void CancelAttack();

		public void RestoreSimulationTimers(float warningElapsed, float activeElapsed, float recoveryElapsed)
		{
			_warningStartTime = Time.time - warningElapsed;
			_attackStartTime = Time.time - activeElapsed;
			_recoveryStartTime = Time.time - recoveryElapsed;
		}
	}
}
