using System;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.HellMaiden.Player.Attacks.ProjectileMovement;
using AstralShift.Helpers;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class BulletProjectile : EnemyAttackStateMachineController
	{
		public bool bulletHasTimeOut;

		public float duration = 10f;

		public float speed = 1f;

		public int pierce = 1;

		public int pierced;

		public Action onAttackFiredEnd;

		private float firedTime;

		protected float elapsedTime;

		protected Vector2 _direction;

		public bool fired;

		public BaseAttackHitEffectResolver hitEffectResolver;

		[SerializeField]
		private bool hitEffectOnExpire = true;

		[SerializeField]
		private GameObject bulletVisual;

		private Vector3 originalScale = Vector3.zero;

		[SerializeField]
		private ParticleSystem bulletParticles;

		[SerializeField]
		private Transform rotationTransform;

		public PM_Base projectileMovement;

		[SerializeField]
		private bool attachedWhileCharging;

		private Vector3 previousPosition;

		private Vector3 currentPosition;

		public BaseEnemyController ShooterController { get; set; }

		public Action OnReturn { get; set; }

		protected override void InitializeStateMachine()
		{
			base.InitializeStateMachine();
			Charging.onEnter = ChargingEnter;
			Charging.onUpdateTick = ChargingOnUpdateTick;
			Fired.onEnter = FireEnter;
			Fired.onExit = FireExit;
			Fired.onFixedUpdateTick = FiredFixedUpdateTick;
			Fired.onUpdateTick = FiredUpdateTick;
			Hit.onEnter = HitEnter;
			Hit.onExit = HitExit;
			Expire.onEnter = ExpireEnter;
			Expire.onExit = ExpireExit;
			End.onEnter = EndEnter;
			_stateMachine.SetInitialState(attachedWhileCharging ? Charging : Fired);
		}

		private void OnEnable()
		{
			fired = false;
			elapsedTime = 0f;
			pierced = 0;
			bulletVisual.transform.position = base.transform.position;
			previousPosition = base.transform.position;
			currentPosition = base.transform.position;
			if (originalScale == Vector3.zero)
			{
				originalScale = base.transform.localScale;
			}
			else
			{
				base.transform.localScale = originalScale;
			}
			if (bulletParticles != null)
			{
				bulletParticles.Clear(withChildren: true);
				bulletParticles.Play();
			}
		}

		public void Fire(Vector2 direction)
		{
			_direction = direction.normalized;
			InitializeStateMachine();
		}

		protected virtual void FixedUpdate()
		{
			_stateMachine?.FixedUpdateTick();
		}

		protected virtual void Update()
		{
			_stateMachine?.UpdateTick();
		}

		private void ChargingEnter()
		{
			PlayStartAnimation(base.TransitionToFire);
		}

		private void ChargingOnUpdateTick()
		{
		}

		public virtual void FireEnter()
		{
			previousPosition = base.transform.position;
			currentPosition = base.transform.position;
			firedTime = Time.time;
			fired = true;
			pierced = 0;
			if (projectileMovement != null)
			{
				projectileMovement.Init(_direction, rotationTransform, speed, duration, (ShooterController != null) ? ShooterController.transform : null);
			}
			if (!attachedWhileCharging)
			{
				PlayStartAnimation(onAttackFiredEnd);
			}
			else
			{
				PlayAttackAnimation();
			}
		}

		public virtual void FireExit()
		{
		}

		private void FiredFixedUpdateTick()
		{
			if (!fired)
			{
				return;
			}
			if (pierced >= pierce)
			{
				fired = false;
				if (hitEffectResolver != null)
				{
					hitEffectResolver.Initialize();
				}
				return;
			}
			previousPosition = base.transform.position;
			if (projectileMovement != null)
			{
				projectileMovement.MovementUpdate(_direction, rotationTransform, speed);
			}
			else
			{
				base.transform.position += (Vector3)(_direction * (speed * Time.fixedDeltaTime));
			}
			currentPosition = base.transform.position;
			elapsedTime = Time.time - firedTime;
			if ((!ProCamera2DHelpers.IsWithinCameraBounds(base.transform.position) || bulletHasTimeOut) && elapsedTime > duration)
			{
				fired = false;
				TransitionToExpire();
			}
		}

		private void FiredUpdateTick()
		{
			if (fired)
			{
				float t = (Time.time - Time.fixedTime) / Time.fixedDeltaTime;
				Vector3 position = Vector3.Lerp(previousPosition, currentPosition, t);
				bulletVisual.transform.position = position;
			}
		}

		public virtual void HitEnter()
		{
			pierced++;
			if (hitEffectResolver != null)
			{
				hitEffectResolver.Initialize();
			}
			if (pierced >= pierce)
			{
				fired = false;
				PlayHitAnimation(base.TransitionToEnd);
			}
			else
			{
				PlayHitAnimation(null);
			}
		}

		public virtual void HitExit()
		{
		}

		public virtual void ExpireEnter()
		{
			PlayEndAnimation(base.TransitionToEnd);
		}

		public virtual void ExpireExit()
		{
		}

		public virtual void EndEnter()
		{
			if (hitEffectOnExpire && hitEffectResolver != null)
			{
				hitEffectResolver.Initialize();
			}
			OnReturn?.Invoke();
		}

		public void StopBulletMovement()
		{
			fired = false;
			TransitionToExpire();
		}

		public void OnHit()
		{
			TransitionToHit();
		}

		private void OnDisable()
		{
			if (bulletParticles != null)
			{
				bulletParticles.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
			}
		}
	}
}
