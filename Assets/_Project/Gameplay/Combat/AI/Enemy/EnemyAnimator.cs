using System;
using System.Collections;
using Animancer;
using AstralShift.Helpers;
using AstralShift.QTI.Helpers.Attributes;
using AstralShift.Rendering;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyAnimator : MonoBehaviour
	{
		protected EnemyController _controller;

		public AnimancerComponent animancer;

		[Header("Move")]
		[SerializeField]
		private ClipTransition moveLeftUp;

		[SerializeField]
		private ClipTransition moveLeftDown;

		[SerializeField]
		private ClipTransition moveRightUp;

		[SerializeField]
		private ClipTransition moveRightDown;

		[Header("Attack Warning")]
		[SerializeField]
		private ClipTransition attackWarningLeftUp;

		[SerializeField]
		private ClipTransition attackWarningLeftDown;

		[SerializeField]
		private ClipTransition attackWarningRightUp;

		[SerializeField]
		private ClipTransition attackWarningRightDown;

		[Header("Attack")]
		[SerializeField]
		protected ClipTransition attackLeftUp;

		[SerializeField]
		protected ClipTransition attackLeftDown;

		[SerializeField]
		protected ClipTransition attackRightUp;

		[SerializeField]
		protected ClipTransition attackRightDown;

		[Header("Recovery")]
		[SerializeField]
		protected ClipTransition recoveryLeftUp;

		[SerializeField]
		protected ClipTransition recoveryLeftDown;

		[SerializeField]
		protected ClipTransition recoveryRightUp;

		[SerializeField]
		protected ClipTransition recoveryRightDown;

		[Header("Hurt")]
		[SerializeField]
		private ClipTransition hurtLeftUp;

		[SerializeField]
		private ClipTransition hurtLeftDown;

		[SerializeField]
		private ClipTransition hurtRightUp;

		[SerializeField]
		private ClipTransition hurtRightDown;

		[Header("Dead")]
		[SerializeField]
		private ClipTransition deadLeftUp;

		[SerializeField]
		private ClipTransition deadLeftDown;

		[SerializeField]
		private ClipTransition deadRightUp;

		[SerializeField]
		private ClipTransition deadRightDown;

		[SerializeField]
		private ClipTransition shadowFadeout;

		[SerializeField]
		private ClipTransition shadowIdle;

		[SerializeField]
		private int shadowAnimationLayer = 5;

		public Animator animator;

		public bool randomAnimatorSpeed;

		[ConditionalHide("randomAnimatorSpeed", false)]
		public float animatorSpeed = 1f;

		[ConditionalHide("randomAnimatorSpeed", true)]
		public Vector2 animatorSpeedBounds = Vector2.one;

		[SerializeField]
		protected SpriteRenderer[] renderers;

		[SerializeField]
		protected SpriteRendererPaletteSwapper paletteSwapper;

		protected Coroutine _hurtBlinkAnimation;

		protected Coroutine _deadBlinkAnimation;

		private WaitForSeconds _hitEffectBlinkWaitInstance = new WaitForSeconds(0.04f);

		protected bool _blockAnimations;

		protected MaterialPropertyBlock _rendererPropertyBlock;

		private Tween _despawnFadeoutTween;

		protected readonly int HitEffectColorSID = Shader.PropertyToID("_HitEffectColor");

		protected readonly int HitEffectBlendSID = Shader.PropertyToID("_HitEffectBlend");

		private Color recolor = Color.white;

		private WaitUntil _deathAnimationHurtBlinkWait;

		public SpriteRenderer[] Renderers => renderers;

		public SpriteRendererPaletteSwapper PaletteSwapper => paletteSwapper;

		public ClipTransition MoveLeftUp
		{
			get
			{
				return moveLeftUp;
			}
			set
			{
				moveLeftUp = value;
			}
		}

		public ClipTransition MoveLeftDown
		{
			get
			{
				return moveLeftDown;
			}
			set
			{
				moveLeftDown = value;
			}
		}

		public ClipTransition MoveRightUp
		{
			get
			{
				return moveRightUp;
			}
			set
			{
				moveRightUp = value;
			}
		}

		public ClipTransition MoveRightDown
		{
			get
			{
				return moveRightDown;
			}
			set
			{
				moveRightDown = value;
			}
		}

		public ClipTransition AttackWarningLeftUp
		{
			get
			{
				return attackWarningLeftUp;
			}
			set
			{
				attackWarningLeftUp = value;
			}
		}

		public ClipTransition AttackWarningLeftDown
		{
			get
			{
				return attackWarningLeftDown;
			}
			set
			{
				attackWarningLeftDown = value;
			}
		}

		public ClipTransition AttackWarningRightUp
		{
			get
			{
				return attackWarningRightUp;
			}
			set
			{
				attackWarningRightUp = value;
			}
		}

		public ClipTransition AttackWarningRightDown
		{
			get
			{
				return attackWarningRightDown;
			}
			set
			{
				attackWarningRightDown = value;
			}
		}

		public virtual float AttackWarningTime => attackWarningLeftUp?.Length ?? 0f;

		public ClipTransition AttackLeftUp
		{
			get
			{
				return attackLeftUp;
			}
			set
			{
				attackLeftUp = value;
			}
		}

		public ClipTransition AttackLeftDown
		{
			get
			{
				return attackLeftDown;
			}
			set
			{
				attackLeftDown = value;
			}
		}

		public ClipTransition AttackRightUp
		{
			get
			{
				return attackRightUp;
			}
			set
			{
				attackRightUp = value;
			}
		}

		public ClipTransition AttackRightDown
		{
			get
			{
				return attackRightDown;
			}
			set
			{
				attackRightDown = value;
			}
		}

		public virtual float AttackTime => attackLeftUp?.Length ?? 0f;

		public ClipTransition RecoveryLeftUp
		{
			get
			{
				return recoveryLeftUp;
			}
			set
			{
				recoveryLeftUp = value;
			}
		}

		public ClipTransition RecoveryLeftDown
		{
			get
			{
				return recoveryLeftDown;
			}
			set
			{
				recoveryLeftDown = value;
			}
		}

		public ClipTransition RecoveryRightUp
		{
			get
			{
				return recoveryRightUp;
			}
			set
			{
				recoveryRightUp = value;
			}
		}

		public ClipTransition RecoveryRightDown
		{
			get
			{
				return recoveryRightDown;
			}
			set
			{
				recoveryRightDown = value;
			}
		}

		public float RecoveryTime => recoveryLeftUp?.Length ?? 0f;

		public ClipTransition HurtLeftUp
		{
			get
			{
				return hurtLeftUp;
			}
			set
			{
				hurtLeftUp = value;
			}
		}

		public ClipTransition HurtLeftDown
		{
			get
			{
				return hurtLeftDown;
			}
			set
			{
				hurtLeftDown = value;
			}
		}

		public ClipTransition HurtRightUp
		{
			get
			{
				return hurtRightUp;
			}
			set
			{
				hurtRightUp = value;
			}
		}

		public ClipTransition HurtRightDown
		{
			get
			{
				return hurtRightDown;
			}
			set
			{
				hurtRightDown = value;
			}
		}

		public ClipTransition DeadLeftUp
		{
			get
			{
				return deadLeftUp;
			}
			set
			{
				deadLeftUp = value;
			}
		}

		public ClipTransition DeadLeftDown
		{
			get
			{
				return deadLeftDown;
			}
			set
			{
				deadLeftDown = value;
			}
		}

		public ClipTransition DeadRightUp
		{
			get
			{
				return deadRightUp;
			}
			set
			{
				deadRightUp = value;
			}
		}

		public ClipTransition DeadRightDown
		{
			get
			{
				return deadRightDown;
			}
			set
			{
				deadRightDown = value;
			}
		}

		public float DeadTime => deadLeftUp.Length;

		public virtual void Init(EnemyController controller)
		{
			_controller = controller;
			animancer.Events.Clear();
			ResetAnimancer();
			if (shadowIdle != null)
			{
				animancer.Layers[shadowAnimationLayer].Play(shadowIdle);
			}
		}

		public void OnDisable()
		{
			animancer.Events.Clear();
			animancer.Stop();
		}

		protected void ResumeAnimator()
		{
			if ((bool)animator)
			{
				if (randomAnimatorSpeed)
				{
					animator.speed = UnityEngine.Random.Range(animatorSpeedBounds.x, animatorSpeedBounds.y);
				}
				else
				{
					animator.speed = animatorSpeed;
				}
			}
		}

		protected void PauseAnimator()
		{
			if ((bool)animator)
			{
				animator.speed = 0f;
			}
		}

		protected virtual void ResetAnimancer()
		{
			_blockAnimations = false;
			animancer.Stop();
		}

		public virtual void Movement(float x, float y)
		{
			// if (!_blockAnimations)
			// {
			// 	ResumeAnimator();
			// 	if (x > 0f)
			// 	{
			// 		animancer.Layers[0].Play((y > 0f) ? moveRightUp : moveRightDown, 0f);
			// 	}
			// 	else
			// 	{
			// 		animancer.Layers[0].Play((y > 0f) ? moveLeftUp : moveLeftDown, 0f);
			// 	}
			// }
		}

		public virtual void AttackWarning(float x, float y)
		{
			if (!_blockAnimations)
			{
				PauseAnimator();
				if (x > 0f)
				{
					animancer.Layers[0].Play((y > 0f) ? attackWarningRightUp : attackWarningRightDown, 0f);
				}
				else
				{
					animancer.Layers[0].Play((y > 0f) ? attackWarningLeftUp : attackWarningLeftDown, 0f);
				}
			}
		}

		public virtual void Attack(float x, float y)
		{
			if (!_blockAnimations)
			{
				if (x > 0f)
				{
					animancer.Layers[0].Play((y > 0f) ? attackRightUp : attackRightDown, 0f);
				}
				else
				{
					animancer.Layers[0].Play((y > 0f) ? attackLeftUp : attackLeftDown, 0f);
				}
			}
		}

		public virtual void Hurt(float x, float y)
		{
			if (!_blockAnimations)
			{
				PauseAnimator();
				if (x > 0f)
				{
					animancer.Layers[0].Play((y > 0f) ? hurtRightUp : hurtRightDown, 0f);
				}
				else
				{
					animancer.Layers[0].Play((y > 0f) ? hurtLeftUp : hurtLeftDown, 0f);
				}
			}
		}

		public virtual void Recovery(float x, float y)
		{
			if (!_blockAnimations)
			{
				if (x > 0f)
				{
					animancer.Layers[0].Play((y > 0f) ? recoveryRightUp : recoveryRightDown, 0f);
				}
				else
				{
					animancer.Layers[0].Play((y > 0f) ? recoveryLeftUp : recoveryLeftDown, 0f);
				}
			}
		}

		public virtual AnimancerState Dead(float x, float y)
		{
			PauseAnimator();
			return animancer.Layers[0].Play(GetDeadClipTransition(x, y), 0f);
		}

		public virtual async UniTask PlayOverridenAnimations(ClipTransition clipTransition, int layer, bool resetOnEnd, bool blockOtherAnimations = false)
		{
			_blockAnimations = blockOtherAnimations;
			await AnimancerHelpers.AnimationTask(animancer, clipTransition, layer);
			if (resetOnEnd)
			{
				ResetAnimancer();
			}
		}

		public void HurtBlinkAnimation()
		{
			if (!(_controller == null) && _controller.isActiveAndEnabled && base.enabled && base.gameObject.activeSelf)
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
			SetRenderersShaderValue(HitEffectBlendSID, 1f);
			SetRenderersShaderValue(HitEffectColorSID, Color.white);
			yield return _hitEffectBlinkWaitInstance;
			SetRenderersShaderValue(HitEffectColorSID, Color.black);
			yield return _hitEffectBlinkWaitInstance;
			SetRenderersShaderValue(HitEffectColorSID, Color.white);
			yield return _hitEffectBlinkWaitInstance;
			SetRenderersShaderValue(HitEffectColorSID, Color.black);
			yield return _hitEffectBlinkWaitInstance;
			ResetHurtBlinkColor();
			_hurtBlinkAnimation = null;
		}

		public virtual void ResetHurtBlinkColor()
		{
			SetRenderersBaseColor(recolor);
			SetRenderersShaderValue(HitEffectBlendSID, 0f);
			SetRenderersShaderValue(HitEffectColorSID, Color.white);
		}

		public ClipTransition GetDeadClipTransition(float x, float y)
		{
			if (x > 0f)
			{
				if (!(y > 0f))
				{
					return deadRightDown;
				}
				return deadRightUp;
			}
			if (!(y > 0f))
			{
				return deadLeftDown;
			}
			return deadLeftUp;
		}

		public virtual void DeathAnimation(Vector2 direction, Action onEnd)
		{
			if (base.enabled && base.gameObject.activeSelf)
			{
				if (_deadBlinkAnimation != null)
				{
					StopCoroutine(_deadBlinkAnimation);
				}
				_deadBlinkAnimation = StartCoroutine(DeathBlinkAnimationCoroutine(direction, onEnd));
			}
		}

		protected virtual IEnumerator DeathBlinkAnimationCoroutine(Vector2 direction, Action onEnd)
		{
			if (_deathAnimationHurtBlinkWait == null)
			{
				_deathAnimationHurtBlinkWait = new WaitUntil(() => _hurtBlinkAnimation == null);
			}
			yield return _deathAnimationHurtBlinkWait;
			ResetHurtBlinkColor();
			AnimancerState state = Dead(direction.x, direction.y);
			float timer = 0f;
			while (state.IsPlayingAndNotEnding() && timer < DeadTime)
			{
				timer += Time.deltaTime;
				yield return null;
			}
			onEnd?.Invoke();
			_deadBlinkAnimation = null;
		}

		public void DeathAnimationShadowFade()
		{
			if (shadowFadeout != null)
			{
				PlayShadowFadeoutAnimation(animancer.Layers[0].CurrentState.RemainingDuration);
			}
		}

		private void PlayShadowFadeoutAnimation(float duration)
		{
			if (shadowFadeout != null && !(shadowFadeout.Clip == null))
			{
				shadowFadeout.Speed = 1f / duration;
				animancer.Layers[shadowAnimationLayer].Play(shadowFadeout);
			}
		}

		public void PlayDeSpawnAnimation(Action onEnd)
		{
			PlayShadowFadeoutAnimation(1f);
			_despawnFadeoutTween?.Kill();
			float alpha = 1f;
			_despawnFadeoutTween = DOTween.To(() => alpha, delegate(float x)
			{
				alpha = x;
				SetRenderersBaseColor(new Color(1f, 1f, 1f, x));
			}, 0f, 1f).SetEase(Ease.InQuad).SetUpdate(isIndependentUpdate: true)
				.OnComplete(delegate
				{
					onEnd?.Invoke();
				});
		}

		public void FinalizeDeSpawnAnimation()
		{
			_despawnFadeoutTween?.Kill();
			_despawnFadeoutTween = null;
			if (shadowIdle != null)
			{
				animancer.Layers[shadowAnimationLayer].Play(shadowIdle);
			}
			SetRenderersBaseColor(Color.white);
		}

		public void SetRenderersBaseColor(Color color)
		{
			for (int i = 0; i < renderers.Length; i++)
			{
				renderers[i].color = color;
			}
		}

		private void SetRenderersShaderValue(int id, float value)
		{
			if (_rendererPropertyBlock == null)
			{
				_rendererPropertyBlock = new MaterialPropertyBlock();
			}
			for (int i = 0; i < renderers.Length; i++)
			{
				SpriteRenderer obj = renderers[i];
				obj.GetPropertyBlock(_rendererPropertyBlock);
				_rendererPropertyBlock.SetFloat(id, value);
				obj.SetPropertyBlock(_rendererPropertyBlock);
			}
		}

		private void SetRenderersShaderValue(int id, Color value)
		{
			if (_rendererPropertyBlock == null)
			{
				_rendererPropertyBlock = new MaterialPropertyBlock();
			}
			for (int i = 0; i < renderers.Length; i++)
			{
				SpriteRenderer obj = renderers[i];
				obj.GetPropertyBlock(_rendererPropertyBlock);
				_rendererPropertyBlock.SetColor(id, value);
				obj.SetPropertyBlock(_rendererPropertyBlock);
			}
		}

		public void Recolor(Texture2D colorLookup)
		{
			if ((bool)PaletteSwapper)
			{
				PaletteSwapper.ColorLut = colorLookup;
			}
		}

		[Obsolete]
		public void Recolor(Color color)
		{
			recolor = color;
			SpriteRenderer[] array = renderers;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].color = recolor;
			}
		}
	}
}
