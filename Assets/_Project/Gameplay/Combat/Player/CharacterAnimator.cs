using System;
using Animancer;
using AstralShift.HellMaiden.Characters.Effects;
using AstralShift.Helpers;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Player
{
	public class CharacterAnimator : MonoBehaviour
	{
		[SerializeField]
		protected AnimancerComponent animancer;

		[Space]
		[SerializeField]
		private SpriteRenderer spriteRenderer;

		[SerializeField]
		private Material defaultMaterial;

		[Space]
		[SerializeField]
		private CharacterFootstepsBehaviour footstepsBehaviour;

		[Header("Idle")]
		[SerializeField]
		protected ClipTransition idleLeftUp;

		[SerializeField]
		protected ClipTransition idleLeftDown;

		[SerializeField]
		protected ClipTransition idleRightUp;

		[SerializeField]
		protected ClipTransition idleRightDown;

		[Header("Run")]
		[SerializeField]
		protected ClipTransition runLeftUp;

		[SerializeField]
		protected ClipTransition runLeftDown;

		[SerializeField]
		protected ClipTransition runRightUp;

		[SerializeField]
		protected ClipTransition runRightDown;

		[Header("Run Transition")]
		[SerializeField]
		protected ClipTransition runTransLeftUp;

		[SerializeField]
		protected ClipTransition runTransLeftDown;

		[SerializeField]
		protected ClipTransition runTransRightUp;

		[SerializeField]
		protected ClipTransition runTransRightDown;

		protected bool blockAnimations;

		private AnimancerState _idleState;

		private AnimancerState _runState;

		public AnimancerComponent Animancer => animancer;

		public AnimancerState IdleState => _idleState;

		public AnimancerState RunState => _runState;

		protected virtual void OnEnable()
		{
			animancer = GetComponent<AnimancerComponent>();
			if (runTransLeftDown.IsValid)
			{
				ref Action onEnd = ref runTransLeftDown.Events.OnEnd;
				onEnd = (Action)Delegate.Combine(onEnd, (Action)delegate
				{
					animancer.Layers[0].Play(runLeftDown);
				});
			}
			if (runTransRightDown.IsValid)
			{
				ref Action onEnd2 = ref runTransRightDown.Events.OnEnd;
				onEnd2 = (Action)Delegate.Combine(onEnd2, (Action)delegate
				{
					animancer.Layers[0].Play(runRightDown);
				});
			}
			if (runTransLeftUp.IsValid)
			{
				ref Action onEnd3 = ref runTransLeftUp.Events.OnEnd;
				onEnd3 = (Action)Delegate.Combine(onEnd3, (Action)delegate
				{
					animancer.Layers[0].Play(runLeftUp);
				});
			}
			if (runTransRightUp.IsValid)
			{
				ref Action onEnd4 = ref runTransRightUp.Events.OnEnd;
				onEnd4 = (Action)Delegate.Combine(onEnd4, (Action)delegate
				{
					animancer.Layers[0].Play(runRightUp);
				});
			}
			if ((bool)footstepsBehaviour)
			{
				footstepsBehaviour.TryCreateEvents(runLeftDown);
				footstepsBehaviour.TryCreateEvents(runRightDown);
				footstepsBehaviour.TryCreateEvents(runLeftUp);
				footstepsBehaviour.TryCreateEvents(runRightUp);
			}
		}

		public void OnDisable()
		{
			animancer.Events.Clear();
			animancer.Stop();
		}

		public virtual void Movement(float v, float x, float y)
		{
			if (!blockAnimations)
			{
				if (v >= 0.2f)
				{
					Run(x, y);
				}
				else
				{
					Idle(x, y);
				}
			}
		}

		public virtual void Run(float x, float y)
		{
			if (blockAnimations || (animancer.Layers[0].CurrentState != null && animancer.Layers[0].CurrentState != _idleState))
			{
				return;
			}
			if (x > 0f)
			{
				if (runTransRightUp.IsValid && runTransRightDown.IsValid)
				{
					_runState = animancer.Layers[0].Play((y > 0f) ? runTransRightUp : runTransRightDown, 0f);
				}
				else
				{
					_runState = animancer.Layers[0].Play((y > 0f) ? runRightUp : runRightDown, 0f);
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
		}

		public virtual void Idle(float x, float y)
		{
			if (!blockAnimations)
			{
				if (x > 0f)
				{
					_idleState = animancer.Layers[0].Play((y > 0f) ? idleRightUp : idleRightDown, 0f);
				}
				else
				{
					_idleState = animancer.Layers[0].Play((y > 0f) ? idleLeftUp : idleLeftDown, 0f);
				}
			}
		}

		public virtual async UniTask PlayOverridenAnimations(ClipTransition clipTransition, int layer, bool resetOnEnd, bool blockOtherAnimations = false)
		{
			blockAnimations = blockOtherAnimations;
			await AnimancerHelpers.AnimationTask(animancer, clipTransition, layer);
			if (resetOnEnd)
			{
				ResetAnimancer();
			}
		}

		public virtual void ResetAnimancer()
		{
			if (spriteRenderer != null && defaultMaterial != null)
			{
				spriteRenderer.material = defaultMaterial;
			}
			blockAnimations = false;
			Animancer.Stop();
		}
	}
}
