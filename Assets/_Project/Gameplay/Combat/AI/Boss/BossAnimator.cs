using System;
using System.Collections;
using Animancer;
using AstralShift.HellMaiden.Player;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss
{
	public class BossAnimator : CharacterAnimator
	{
		[Header("Hurt")]
		[SerializeField]
		private ClipTransition hurtLeftUp;

		[SerializeField]
		private ClipTransition hurtLeftDown;

		[SerializeField]
		private ClipTransition hurtRightUp;

		[SerializeField]
		private ClipTransition hurtRightDown;

		[Header("Immunity")]
		[SerializeField]
		private ClipTransition immunity;

		[Header("Defeat")]
		[SerializeField]
		private ClipTransition defeat;

		[Header("Attack")]
		[SerializeField]
		private ClipTransition idleToWarning;

		[SerializeField]
		private ClipTransition warning;

		[SerializeField]
		private ClipTransition warningToAttacking;

		[SerializeField]
		private ClipTransition attacking;

		[Space]
		[SerializeField]
		protected SpriteRenderer[] renderers;

		protected Coroutine _hurtBlinkAnimation;

		protected readonly int HitEffectColorSID = Shader.PropertyToID("_HitEffectColor");

		protected readonly int HitEffectBlendSID = Shader.PropertyToID("_HitEffectBlend");

		[SerializeField]
		protected float animatorSpeed = 1f;

		protected bool _canTranstion;

		private void Start()
		{
			animancer.Graph.Speed = animatorSpeed;
			if (idleToWarning.IsValid)
			{
				ref Action onEnd = ref idleToWarning.Events.OnEnd;
				onEnd = (Action)Delegate.Combine(onEnd, (Action)delegate
				{
					animancer.Layers[0].Play(warning);
				});
			}
			if (warningToAttacking.IsValid)
			{
				ref Action onEnd2 = ref warningToAttacking.Events.OnEnd;
				onEnd2 = (Action)Delegate.Combine(onEnd2, (Action)delegate
				{
					animancer.Layers[0].Play(attacking);
				});
			}
		}

		public void ResetAnimationCallbacks()
		{
			warning.Events.OnEnd = null;
			attacking.Events.OnEnd = null;
		}

		public override void Run(float x, float y)
		{
			if (x > 0f)
			{
				if (runTransRightUp.IsValid && runTransRightDown.IsValid)
				{
					animancer.Layers[0].Play((y > 0f) ? runTransRightUp : runTransRightDown, 0f);
				}
				else
				{
					animancer.Layers[0].Play((y > 0f) ? runRightUp : runRightDown, 0f);
				}
			}
			else if (runTransLeftUp.IsValid && runTransLeftDown.IsValid)
			{
				animancer.Layers[0].Play((y > 0f) ? runTransLeftUp : runTransLeftDown, 0f);
			}
			else
			{
				animancer.Layers[0].Play((y > 0f) ? runLeftUp : runLeftDown, 0f);
			}
			_canTranstion = false;
		}

		public virtual void Hurt(float x, float y)
		{
			if (x > 0f)
			{
				animancer.Layers[0].Play((y > 0f) ? hurtRightUp : hurtRightDown, 0f);
			}
			else
			{
				animancer.Layers[0].Play((y > 0f) ? hurtLeftUp : hurtLeftDown, 0f);
			}
		}

		public virtual void AttackWarning(float x, float y, Action onEnd = null)
		{
			ref Action onEnd2 = ref warning.Events.OnEnd;
			onEnd2 = (Action)Delegate.Combine(onEnd2, new Action(OnEnd));
			if (idleToWarning.IsValid)
			{
				animancer.Layers[0].Play(idleToWarning);
			}
			else
			{
				animancer.Layers[0].Play(warning);
			}
			_canTranstion = false;
			void OnEnd()
			{
				onEnd?.Invoke();
				ref Action onEnd3 = ref warning.Events.OnEnd;
				onEnd3 = (Action)Delegate.Remove(onEnd3, new Action(OnEnd));
			}
		}

		public virtual void Attack(float x, float y, Action onEnd = null)
		{
			ref Action onEnd2 = ref attacking.Events.OnEnd;
			onEnd2 = (Action)Delegate.Combine(onEnd2, new Action(OnEnd));
			if (warningToAttacking.IsValid)
			{
				animancer.Layers[0].Play(warningToAttacking);
			}
			else
			{
				animancer.Layers[0].Play(attacking);
			}
			_canTranstion = false;
			void OnEnd()
			{
				onEnd?.Invoke();
				ref Action onEnd3 = ref attacking.Events.OnEnd;
				onEnd3 = (Action)Delegate.Remove(onEnd3, new Action(OnEnd));
			}
		}

		public void Dead()
		{
			Dead(null);
		}

		public virtual void Dead(Action onEnd)
		{
			ref Action onEnd2 = ref defeat.Events.OnEnd;
			onEnd2 = (Action)Delegate.Combine(onEnd2, new Action(OnEnd));
			animancer.Layers[0].Play(defeat);
			void OnEnd()
			{
				onEnd?.Invoke();
				ref Action onEnd3 = ref defeat.Events.OnEnd;
				onEnd3 = (Action)Delegate.Remove(onEnd3, new Action(OnEnd));
			}
		}

		public void SetImmunity(bool state)
		{
			if (immunity.IsValid)
			{
				if (state)
				{
					animancer.Layers[2].Play(immunity, 0f);
				}
				else
				{
					animancer.Layers[2].Stop();
				}
				_canTranstion = false;
			}
		}

		public void HurtBlinkAnimation()
		{
			if (base.enabled && base.gameObject.activeSelf)
			{
				if (_hurtBlinkAnimation != null)
				{
					StopCoroutine(_hurtBlinkAnimation);
				}
				_hurtBlinkAnimation = StartCoroutine(HurtBlinkAnimationCoroutine());
			}
		}

		protected virtual IEnumerator HurtBlinkAnimationCoroutine()
		{
			WaitForSeconds waitInstance = new WaitForSeconds(0.04f);
			SetRenderersShaderValue(HitEffectBlendSID, 1f);
			SetRenderersShaderValue(HitEffectColorSID, Color.white);
			yield return waitInstance;
			SetRenderersShaderValue(HitEffectColorSID, Color.black);
			yield return waitInstance;
			SetRenderersShaderValue(HitEffectColorSID, Color.white);
			yield return waitInstance;
			SetRenderersShaderValue(HitEffectColorSID, Color.black);
			yield return waitInstance;
			ResetHurtBlinkColor();
			_hurtBlinkAnimation = null;
		}

		protected virtual void ResetHurtBlinkColor()
		{
			SetRenderersBaseColor(Color.white);
			SetRenderersShaderValue(HitEffectBlendSID, 0f);
			SetRenderersShaderValue(HitEffectColorSID, Color.white);
		}

		private void SetRenderersBaseColor(Color color)
		{
			for (int i = 0; i < renderers.Length; i++)
			{
				renderers[i].color = color;
			}
		}

		private void SetRenderersShaderValue(int id, float value)
		{
			for (int i = 0; i < renderers.Length; i++)
			{
				renderers[i].material.SetFloat(id, value);
			}
		}

		private void SetRenderersShaderValue(int id, Color value)
		{
			for (int i = 0; i < renderers.Length; i++)
			{
				renderers[i].material.SetColor(id, value);
			}
		}

		public new virtual void Idle(float x, float y)
		{
			_canTranstion = true;
			StartCoroutine(TryTransitionToIdle(x, y));
		}

		public virtual void ForceIdle()
		{
			_canTranstion = true;
			animancer.Layers[0].Play(idleRightDown, 0f);
		}

		public IEnumerator TryTransitionToIdle(float x, float y)
		{
			yield return new WaitForSeconds(1f);
			if (_canTranstion && (animancer.Layers[0].CurrentState == runLeftDown.State || animancer.Layers[0].CurrentState == runLeftUp.State || animancer.Layers[0].CurrentState == runRightDown.State || animancer.Layers[0].CurrentState == runRightUp.State))
			{
				if (x > 0f)
				{
					animancer.Layers[0].Play((y > 0f) ? idleRightUp : idleRightDown, 0f);
				}
				else
				{
					animancer.Layers[0].Play((y > 0f) ? idleLeftUp : idleLeftDown, 0f);
				}
			}
		}
	}
}
