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

		public virtual void Attack(Vector2 direction, float speed, int hitMaxCount, bool rotateToDirection)
		{
			PlayerMovement owner = _behaviour.OwnerPlayer;
			if (owner == null)
			{
				throw new System.InvalidOperationException(
					"ProjectileAttack requires an owning PlayerMovement.");
			}

			_direction = direction.normalized;
			_lastPlayerDir = owner.attackDirection.normalized;
			this.speed = speed;
			base.rotateToDirection = rotateToDirection;
			this.hitMaxCount = hitMaxCount;
			if (!fixedDuration)
			{
				despawnTimeout = NativeAttackSnapshot != null
					? NativeAttackSnapshot.Stats.Duration
					: _behaviour.DurationValue;
			}
			projectileMovement?.Init(_direction, rotationTransform, speed, despawnTimeout, owner.transform);
			PlayParticleSystem();
			ResetParameters();
			Attack(direction, base.rotateToDirection);
		}

		protected override void OnHit(IDamageable damageable)
		{
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
			else if ((bool)attackAnim.Clip)
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
