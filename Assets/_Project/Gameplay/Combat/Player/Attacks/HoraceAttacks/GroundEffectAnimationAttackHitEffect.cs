using System;
using Animancer;
using AstralShift.Helpers;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks.HoraceAttacks
{
	public class GroundEffectAnimationAttackHitEffect : BaseAttackHitEffect
	{
		public ClipTransition attackStartAnim;

		[SerializeField]
		[Range(0f, 1f)]
		private int startAnimLayer;

		public ClipTransition attackLoopAnim;

		[SerializeField]
		[Range(0f, 1f)]
		private int loopAnimLayer;

		public ClipTransition attackEndAnim;

		[SerializeField]
		[Range(0f, 1f)]
		private int endAnimLayer;

		public int hitAnimLayer;

		public ProjectileAttack Projectile;

		public AnimancerComponent animancer;

		private Action _onEnd;

		[SerializeField]
		private Transform rotationTransform;

		private float _duration;

		[SerializeField]
		private float durationMultiplier = 1f;

		[Header("Sounds")]
		[SerializeField]
		private EventReference groundEffectSound;

		public override void Init(WeaponBehaviour behaviour)
		{
			rotationTransform.eulerAngles = Projectile.RotationTransform.eulerAngles;
			if ((bool)progressionScaler)
			{
				progressionScaler.Apply(behaviour);
			}
			_duration = behaviour.DurationValue;
		}

		public override void PlayOnEnable(Action onEnd)
		{
			throw new NotImplementedException();
		}

		public override void PlayOnEnable(Action<IDamageable> onHit, Action onEnd)
		{
			ref Action onEnd2 = ref attackStartAnim.Events.OnEnd;
			onEnd2 = (Action)Delegate.Combine(onEnd2, (Action)delegate
			{
				animancer.Layers[loopAnimLayer].Play(attackLoopAnim, attackLoopAnim.FadeDuration);
			});
			animancer.Layers[startAnimLayer].Play(attackStartAnim, attackStartAnim.FadeDuration);
			if (base.gameObject.activeSelf)
			{
				Play(onHit, onEnd);
			}
			StartCoroutine(Wait.SetTimeout(_duration * durationMultiplier, Stop));
		}

		public override void Play(Action onEnd)
		{
			throw new NotImplementedException();
		}

		public override void Play(Action<IDamageable> onHit, Action onEnd)
		{
			_onEnd = onEnd;
			if ((bool)hitbox)
			{
				hitbox.Init(onHit);
			}
		}

		public override void Stop()
		{
			if ((bool)attackEndAnim.Clip)
			{
				attackEndAnim.Events.OnEnd = _onEnd;
				animancer.Layers[loopAnimLayer].Stop();
				animancer.Layers[endAnimLayer].Play(attackEndAnim, attackEndAnim.FadeDuration);
			}
			else
			{
				_onEnd?.Invoke();
			}
		}

		public void PlayGroundEffectSound()
		{
			if (!groundEffectSound.IsNull)
			{
				RuntimeManager.PlayOneShot(groundEffectSound, base.transform.position);
			}
		}
	}
}
