using System;
using Animancer;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss
{
	public class AnimatedBossAttack : MonoBehaviour
	{
		[Header("References")]
		[SerializeField]
		protected AnimancerComponent animancer;

		[Header("Isometric Settings")]
		[SerializeField]
		protected bool isometricRotation = true;

		[SerializeField]
		[ConditionalHide("isometricRotation", true)]
		protected float isometricAngle = -45f;

		[SerializeField]
		protected bool rotateToDirection;

		[SerializeField]
		protected Transform rotationTransform;

		protected Vector2 _currentDirection;

		[Header("Animation Settings")]
		public ClipTransition deactivated;

		public ClipTransition inAnimation;

		public ClipTransition loopAnimation;

		public ClipTransition outAnimation;

		public ClipTransition hitAnimation;

		private Action inAnimationOnEnd;

		private Action outAnimationOnEnd;

		private Action hitAnimationOnEnd;

		[SerializeField]
		protected float animationSpeed = 1f;

		public AnimancerComponent Animancer => animancer;

		public Vector2 CurrentDirection => _currentDirection;

		public virtual void RunInAnimation(Action onEnd = null)
		{
			animancer.Graph.Speed = animationSpeed;
			if (inAnimation.IsValid)
			{
				inAnimationOnEnd = onEnd;
				inAnimation.Events.OnEnd = OnEndAction;
				animancer.Play(inAnimation, inAnimation.FadeDuration);
			}
			else
			{
				onEnd?.Invoke();
			}
			void OnEndAction()
			{
				inAnimationOnEnd?.Invoke();
				inAnimationOnEnd = null;
			}
		}

		public virtual void RunLoopAnimation()
		{
			if (loopAnimation.IsValid)
			{
				animancer.Play(loopAnimation, loopAnimation.FadeDuration);
			}
		}

		public virtual void RunOutAnimation(Action onEnd = null)
		{
			if (outAnimation.IsValid)
			{
				outAnimationOnEnd = onEnd;
				outAnimation.Events.OnEnd = OnEndAction;
				if (animancer != null)
				{
					animancer.Layers[0]?.Play(outAnimation, outAnimation.FadeDuration);
				}
			}
			else
			{
				onEnd?.Invoke();
			}
			void OnEndAction()
			{
				outAnimationOnEnd?.Invoke();
				outAnimationOnEnd = null;
			}
		}

		public virtual void RunDeactivatedAnimation(Action onEnd = null)
		{
			if (deactivated.IsValid)
			{
				outAnimationOnEnd = onEnd;
				deactivated.Events.OnEnd = OnEndAction;
				animancer.Layers[0].Play(deactivated);
			}
			else
			{
				onEnd?.Invoke();
			}
			void OnEndAction()
			{
				outAnimationOnEnd?.Invoke();
				outAnimationOnEnd = null;
			}
		}

		public virtual void RunHitAnimation(Action onEnd = null, bool isAdditive = false)
		{
			if (hitAnimation.IsValid)
			{
				hitAnimationOnEnd = onEnd;
				hitAnimation.Events.OnEnd = OnEndAction;
				if (isAdditive)
				{
					animancer.Layers[1].Play(hitAnimation, hitAnimation.FadeDuration);
				}
				else
				{
					animancer.Layers[0].Play(hitAnimation, hitAnimation.FadeDuration);
				}
			}
			else
			{
				onEnd?.Invoke();
			}
			void OnEndAction()
			{
				hitAnimationOnEnd?.Invoke();
				hitAnimationOnEnd = null;
			}
		}

		public virtual void Rotate(Vector2 direction)
		{
			_currentDirection = direction;
			if ((bool)rotationTransform)
			{
				rotationTransform.eulerAngles = new Vector3(isometricAngle, base.transform.eulerAngles.y, Vector2.SignedAngle(Vector2.right, _currentDirection));
			}
			else
			{
				base.transform.eulerAngles = new Vector3(isometricAngle, base.transform.eulerAngles.y, Vector2.SignedAngle(Vector2.right, _currentDirection));
			}
		}
	}
}
