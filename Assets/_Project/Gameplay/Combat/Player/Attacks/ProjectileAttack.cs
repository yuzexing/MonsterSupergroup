using System;
using AstralShift.HellMaiden.Player.Attacks.ProjectileMovement;
using AstralShift.Helpers;
using AstralShift.Helpers.Attributes;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class ProjectileAttack : AnimatedAttack
	{
		[Header("Movement Settings")]
		[SerializeField]
		[ReadOnly]
		[Tooltip("Comes from WeaponData.")]
		protected float speed;

		[SerializeField]
		[ReadOnly]
		[Tooltip("Comes from WeaponBehaviour.")]
		protected int hitMaxCount = 1;

		[SerializeField]
		protected bool fixedDuration;

		[SerializeField]
		[Tooltip("Comes from WeaponData. Or is setup here in the inspector if fixedDuration is true")]
		protected float despawnTimeout = 10f;

		protected bool isCharging;

		private int _hitCount;

		protected Vector2 _direction;

		private float _firedTime;

		private float _elapsedTimeout;

		private Vector2 _lastPlayerDir;

		[SerializeField]
		private bool onlyDespawnOffCamera = true;

		[SerializeField]
		private bool hitEffectOnExpire;

		[SerializeField]
		public PM_Base projectileMovement;

		[SerializeField]
		private ParticleSystem particleSystem;

		[SerializeField]
		private bool attachedToPlayerWhileStartAnimationPlays;

		[Header("Sounds")]
		[SerializeField]
		private AnimatedAttackSound chargeSound;

		[SerializeField]
		private AnimatedAttackSound launchSound;

		[SerializeField]
		private AnimatedAttackSound projectileLoopSound;

		[SerializeField]
		private AnimatedAttackSound projectileHitSound;

		[SerializeField]
		private AnimatedAttackSound expireSound;

		private EventInstance _loopInstance;

		private ProjectilePresentationKey _presentationKey;

		private Action<
			ProjectilePresentationKey,
			Vector3,
			ProjectilePresentationPhase> _presentationTermination;

		private bool _terminationPublished;

		private bool _presentationTerminated;

		public float DespawnTimeout
		{
			get
			{
				return despawnTimeout;
			}
			private set
			{
				despawnTimeout = value;
			}
		}

		protected bool _fired { get; set; }

		public bool PresentationOnly => IsPresentationOnly;

		public void ConfigurePresentationLifecycle(
			ProjectilePresentationKey key,
			Action<
				ProjectilePresentationKey,
				Vector3,
				ProjectilePresentationPhase> onTermination)
		{
			if (!key.IsValid)
			{
				throw new System.ArgumentException(
					"Projectile presentation key must be valid.",
					nameof(key));
			}

			_presentationKey = key;
			_presentationTermination = onTermination ??
				throw new System.ArgumentNullException(nameof(onTermination));
			_terminationPublished = false;
		}

		public virtual void Attack(Vector2 direction, float speed, int hitMaxCount, bool rotateToDirection)
		{
			PlayerMovement owner = _behaviour.OwnerPlayer;
			if (owner == null)
			{
				throw new System.InvalidOperationException(
					"ProjectileAttack requires an owning PlayerMovement.");
			}

			_direction = direction.normalized;
			_presentationTerminated = false;
			_lastPlayerDir = owner.attackDirection.normalized;
			this.speed = speed;
			base.rotateToDirection = rotateToDirection;
			this.hitMaxCount = hitMaxCount;
			if (!fixedDuration)
			{
				despawnTimeout = NativeAttackSnapshot?.Stats.Duration ??
					throw new System.InvalidOperationException(
						"Owned projectile has no New GAS AttackSnapshot.");
			}
			projectileMovement?.Init(_direction, rotationTransform, speed, despawnTimeout, owner.transform);
			PlayParticleSystem();
			ResetParameters();
			Attack(direction, base.rotateToDirection);
		}

		public void PlayPresentation(
			ProjectilePresentationSpawn spawn,
			float elapsedSeconds)
		{
			if (!IsPresentationOnly)
			{
				throw new System.InvalidOperationException(
					"PlayPresentation requires InitPresentation first.");
			}

			PlayerMovement owner = _behaviour.OwnerPlayer;
			if (owner == null)
			{
				throw new System.InvalidOperationException(
					"Projectile presentation requires an owning PlayerMovement.");
			}

			_direction = spawn.Direction.normalized;
			_lastPlayerDir = owner.attackDirection.normalized;
			speed = spawn.Stats.EffectiveSpeed;
			base.rotateToDirection = spawn.RotateToMovement;
			hitMaxCount = int.MaxValue;
			if (!fixedDuration)
			{
				despawnTimeout = spawn.Stats.Duration;
			}

			projectileMovement?.Init(
				_direction,
				rotationTransform,
				speed,
				despawnTimeout,
				owner.transform);
			PlayParticleSystem();
			ResetPresentationParameters();
			Attack(_direction, base.rotateToDirection);
			FastForwardPresentation(Mathf.Max(0f, elapsedSeconds));
		}

		protected override void OnHit(IDamageable damageable)
		{
			if (IsPresentationOnly)
			{
				return;
			}

			if (damageable != null)
			{
				_hitCount++;
				if (_hitCount < hitMaxCount)
				{
					ResolveDamage(damageable);
					return;
				}
				if (base.HitEffectMode != DamageMode.None && base.HitEffectMode != DamageMode.ExplosionHit)
				{
					ResolveDamage(damageable);
				}
			}
			PublishTermination(ProjectilePresentationPhase.Hit);
			_fired = false;
			if (_loopInstance.isValid())
			{
				_loopInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
			}
			ResolveHitEffect();
			PlayEndAnimation();
		}

		private void Update()
		{
			if (IsPresentationOnly && _presentationTerminated)
			{
				return;
			}

			if (Time.deltaTime == 0f)
			{
				return;
			}
			if (!isCharging && attackStartAnimTransitionAfterFinish && attachedToPlayerWhileStartAnimationPlays)
			{
				PlayerMovement owner = _behaviour.OwnerPlayer;
				if (owner == null)
				{
					return;
				}
				base.transform.position = owner.transform.position;
				Vector2 normalized = owner.attackDirection.normalized;
				float z = Vector2.SignedAngle(_lastPlayerDir, normalized);
				_direction = Quaternion.Euler(0f, 0f, z) * _direction;
				_lastPlayerDir = normalized;
				return;
			}
			if (isometricRotation)
			{
				_ = isometricAngle;
			}
			if (projectileMovement != null)
			{
				projectileMovement.MovementUpdate(_direction, rotationTransform, speed);
			}
			else
			{
				base.transform.position += (Vector3)(_direction * (speed * Time.smoothDeltaTime));
				UpdateRotation(_direction);
			}
			if (onlyDespawnOffCamera && ProCamera2DHelpers.IsWithinCameraBounds(base.transform.position))
			{
				_elapsedTimeout = 0f;
				return;
			}
			_elapsedTimeout += Time.deltaTime;
			if (_elapsedTimeout > despawnTimeout)
			{
				EndProjectile();
			}
		}

		protected virtual void EndProjectile()
		{
			if (IsPresentationOnly)
			{
				TerminatePresentation(
					ProjectilePresentationPhase.Expired,
					base.transform.position);
				return;
			}

			PublishTermination(ProjectilePresentationPhase.Expired);
			_fired = false;
			if (!onlyDespawnOffCamera)
			{
				if (hitEffectOnExpire)
				{
					OnExpireHitEffect();
					return;
				}
				if (expireSound.automatic)
				{
					PlayExpireSound();
				}
				PlayEndAnimation();
			}
			else
			{
				EndCallback();
			}
		}

		protected void ResetParameters()
		{
			_firedTime = Time.time;
			_elapsedTimeout = 0f;
			_hitCount = 0;
			if (!fixedDuration)
			{
				despawnTimeout = NativeAttackSnapshot != null
					? NativeAttackSnapshot.Stats.Duration
					: _behaviour.DurationValue;
			}
			speed *= NativeAttackSnapshot != null
				? NativeAttackSnapshot.Stats.SpeedMultipliersProduct
				: _behaviour.SpeedMultipliersProduct;
			if (attackStartAnim == null || !attackStartAnimTransitionAfterFinish)
			{
				if (launchSound.automatic)
				{
					PlayLaunchSound();
				}
				if (projectileLoopSound.automatic)
				{
					PlayLaunchedLoopSound();
				}
				isCharging = true;
				_fired = true;
			}
			else
			{
				isCharging = false;
				if (chargeSound.automatic)
				{
					PlayChargeSound();
				}
			}
		}

		private void ResetPresentationParameters()
		{
			_firedTime = Time.time;
			_elapsedTimeout = 0f;
			_hitCount = 0;
			_presentationTerminated = false;
			if (attackStartAnim == null || !attackStartAnimTransitionAfterFinish)
			{
				if (launchSound.automatic)
				{
					PlayLaunchSound();
				}
				if (projectileLoopSound.automatic)
				{
					PlayLaunchedLoopSound();
				}
				isCharging = true;
				_fired = true;
			}
			else
			{
				isCharging = false;
				if (chargeSound.automatic)
				{
					PlayChargeSound();
				}
			}
		}

		private void FastForwardPresentation(float elapsedSeconds)
		{
			if (elapsedSeconds <= 0f || projectileMovement != null ||
				(attackStartAnim != null && attackStartAnimTransitionAfterFinish))
			{
				return;
			}

			_firedTime -= elapsedSeconds;
			base.transform.position +=
				(Vector3)(_direction * (speed * elapsedSeconds));
			UpdateRotation(_direction);
		}

		public void TerminatePresentation(
			ProjectilePresentationPhase phase,
			Vector3 finalPosition)
		{
			if (!IsPresentationOnly ||
				phase == ProjectilePresentationPhase.Spawn ||
				_presentationTerminated)
			{
				return;
			}

			_presentationTerminated = true;
			_fired = false;
			base.transform.position = finalPosition;
			if (_loopInstance.isValid())
			{
				_loopInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
			}

			if (phase == ProjectilePresentationPhase.Cancelled)
			{
				StopParticleSystem();
				EndCallback();
				return;
			}

			if (phase == ProjectilePresentationPhase.Hit &&
				projectileHitSound.automatic)
			{
				PlayHitSound();
			}
			else if (phase == ProjectilePresentationPhase.Expired &&
				expireSound.automatic)
			{
				PlayExpireSound();
			}

			PlayEndAnimation();
		}

		private void PublishTermination(ProjectilePresentationPhase phase)
		{
			if (_terminationPublished || _presentationTermination == null)
			{
				return;
			}

			_terminationPublished = true;
			_presentationTermination.Invoke(
				_presentationKey,
				base.transform.position,
				phase);
		}

		protected void PlayParticleSystem()
		{
			if ((bool)particleSystem)
			{
				particleSystem.Play();
			}
		}

		protected void StopParticleSystem()
		{
			if ((bool)particleSystem)
			{
				particleSystem.Stop();
				particleSystem.Clear();
			}
		}

		protected override void OnDisable()
		{
			if (!IsPresentationOnly)
			{
				PublishTermination(ProjectilePresentationPhase.Cancelled);
			}
			_presentationTermination = null;
			_presentationKey = default;
			ReleaseNativeAttackSnapshot();
			base.OnDisable();
			StopParticleSystem();
			if (_loopInstance.isValid())
			{
				_loopInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
				_loopInstance.release();
			}
		}

		public override void PlayAttackAnimation()
		{
			if (attackStartAnimTransitionAfterFinish)
			{
				if (launchSound.automatic)
				{
					PlayLaunchSound();
				}
				if (projectileLoopSound.automatic)
				{
					PlayLaunchedLoopSound();
				}
				isCharging = true;
				_fired = true;
				if (!attachedToPlayerWhileStartAnimationPlays)
				{
					base._attackAnimDuration = despawnTimeout;
					base.PlayAttackAnimation();
				}
			}
			else if (attackAnim != null && (bool)attackAnim.Clip)
			{
				base._attackAnimDuration = despawnTimeout;
				base.PlayAttackAnimation();
			}
		}

		public virtual void OnExpireHitEffect()
		{
			if (expireSound.automatic)
			{
				PlayExpireSound();
			}
			ResolveHitEffect();
			PlayEndAnimation();
		}

		protected virtual void ResolveHitEffect()
		{
			if (projectileHitSound.automatic)
			{
				PlayHitSound();
			}
			if ((bool)hitEffectResolver)
			{
				if (NativeAttackSnapshot != null)
				{
					hitEffectResolver.Initialize(_behaviour, NativeAttackSnapshot);
				}
				else
				{
					hitEffectResolver.Initialize(_behaviour);
				}
			}
		}

		public void PlayChargeSound()
		{
			if (!chargeSound.eventRef.IsNull)
			{
				RuntimeManager.PlayOneShotAttached(chargeSound.eventRef, base.gameObject);
			}
		}

		public void PlayLaunchSound()
		{
			if (!launchSound.eventRef.IsNull)
			{
				RuntimeManager.PlayOneShot(launchSound.eventRef, base.transform.position);
			}
		}

		public void PlayLaunchedLoopSound()
		{
			if (!projectileLoopSound.eventRef.IsNull)
			{
				if (!_loopInstance.isValid())
				{
					_loopInstance = RuntimeManager.CreateInstance(projectileLoopSound.eventRef);
				}
				RuntimeManager.AttachInstanceToGameObject(_loopInstance, base.gameObject);
				_loopInstance.start();
			}
		}

		public void PlayHitSound()
		{
			if (!projectileHitSound.eventRef.IsNull)
			{
				RuntimeManager.PlayOneShot(projectileHitSound.eventRef, base.transform.position);
			}
		}

		public void PlayExpireSound()
		{
			if (!expireSound.eventRef.IsNull)
			{
				RuntimeManager.PlayOneShot(expireSound.eventRef, base.transform.position);
			}
		}
	}
}
