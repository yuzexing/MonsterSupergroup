using System;
using System.Collections;
using System.Collections.Generic;
using Animancer;
using AstralShift.HellMaiden.Interactions;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyAttackPrefab : MonoBehaviour
	{
		private enum DamageApplyMode
		{
			Interaction = 0,
			HitBox = 1
		}

		[SerializeField]
		private DamageApplyMode damageApplyMode;

		public PlayerDamageInteraction damageInteraction;

		public BaseAttackHitBox hitBox;

		public EnemyAttackWarning attackWarning;

		[Header("Animation Settings")]
		[SerializeField]
		protected AnimancerComponent animancer;

		public ClipTransition attackStartAnim;

		public int startAnimLayer;

		public bool attackStartAnimTransitionAfterFinish;

		public ClipTransition attackAnim;

		public int attackAnimLayer;

		public bool attackAnimTransitionAfterFinish;

		public ClipTransition attackEndAnim;

		public int attackEndAnimLayer;

		public ClipTransition attackHitAnim;

		public int hitAnimLayer;

		private int _animationsToFinish;

		protected Coroutine _timeoutAnimationCoroutine;

		protected EnemyStats _stats;

		public void SetStats(EnemyStats stats)
		{
			_stats = stats;
			switch (damageApplyMode)
			{
			case DamageApplyMode.Interaction:
				if (damageInteraction != null)
				{
					damageInteraction.enemyStats = stats;
				}
				break;
			case DamageApplyMode.HitBox:
				if (hitBox != null)
				{
					hitBox.Init(OnHit);
				}
				break;
			}
		}

		public void OnHit(IDamageable damageable)
		{
			damageable.Damage(_stats.Damage, DamageType.Normal);
		}

		public void Hide()
		{
			base.gameObject.SetActive(value: false);
		}

		protected void PlayStartAnimation(Action onAttackFiredEnd)
		{
			if (!attackStartAnim.Clip)
			{
				PlayAttackAnimation();
				return;
			}
			AnimancerState currentState = animancer.Layers[startAnimLayer].Play(attackStartAnim, attackStartAnim.FadeDuration);
			if (attackStartAnimTransitionAfterFinish)
			{
				currentState.Events(this).OnEnd = delegate
				{
					PlayAttackAnimation();
					EndCallback(onAttackFiredEnd);
					currentState.Events(this).OnEnd = null;
				};
			}
		}

		protected virtual void PlayAttackAnimation()
		{
			if ((bool)attackAnim.Clip)
			{
				List<AnimancerState> list = new List<AnimancerState>();
				AnimancerState animancerState = animancer.Layers[attackAnimLayer].Play(attackAnim, attackAnim.FadeDuration);
				list.Add(animancerState);
				if (attackAnimTransitionAfterFinish)
				{
					animancerState.Events(this).OnEnd = CheckEndOfAnimations;
				}
				_animationsToFinish = list.Count;
			}
		}

		protected void PlayEndAnimation(Action onExpireEnd)
		{
			if (!attackEndAnim.Clip)
			{
				EndCallback(onExpireEnd);
				return;
			}
			AnimancerState currentState = animancer.Layers[attackEndAnimLayer].Play(attackEndAnim, attackEndAnim.FadeDuration);
			currentState.Events(this).OnEnd = delegate
			{
				EndCallback(onExpireEnd);
				currentState.Events(this).OnEnd = null;
			};
		}

		protected void PlayHitAnimation(Action onHitEnd)
		{
			if (!attackHitAnim.Clip)
			{
				EndCallback(onHitEnd);
				return;
			}
			float length = animancer.Layers[hitAnimLayer].Play(attackHitAnim, attackHitAnim.FadeDuration).Length;
			StartCoroutine(HitAnimRoutine(length, onHitEnd));
		}

		protected IEnumerator HitAnimRoutine(float duration, Action onHitEnd)
		{
			yield return new WaitForSeconds(duration);
			EndCallback(onHitEnd);
		}

		private void CheckEndOfAnimations()
		{
			_animationsToFinish--;
			if (_animationsToFinish <= 0 && attackAnimTransitionAfterFinish && _timeoutAnimationCoroutine != null)
			{
				StopCoroutine(_timeoutAnimationCoroutine);
				_timeoutAnimationCoroutine = null;
			}
		}

		protected void EndCallback(Action action)
		{
			action?.Invoke();
		}

		public void EnableDamage()
		{
			if ((bool)hitBox)
			{
				hitBox.Toggle(state: true);
			}
		}
	}
}
