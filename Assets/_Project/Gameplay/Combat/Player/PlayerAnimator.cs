using Animancer;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Player
{
	public class PlayerAnimator : CharacterAnimator
	{
		[Header("Dash")]
		[SerializeField]
		protected ClipTransition dashLeftUp;

		[SerializeField]
		protected ClipTransition dashLeftDown;

		[SerializeField]
		protected ClipTransition dashRightUp;

		[SerializeField]
		protected ClipTransition dashRightDown;

		[Header("Hurt")]
		[SerializeField]
		protected ClipTransition hurtLeftUp;

		[SerializeField]
		protected ClipTransition hurtLeftDown;

		[SerializeField]
		protected ClipTransition hurtRightUp;

		[SerializeField]
		protected ClipTransition hurtRightDown;

		[Header("Dead")]
		[SerializeField]
		protected ClipTransition deadLeftUp;

		[SerializeField]
		protected ClipTransition deadLeftDown;

		[SerializeField]
		protected ClipTransition deadRightUp;

		[SerializeField]
		protected ClipTransition deadRightDown;

		[Header("Miscelaneous")]
		[SerializeField]
		protected ClipTransition teleport;

		private CartesianMixerState _idleMixerState;

		private CartesianMixerState _runMixerState;

		protected override void OnEnable()
		{
			base.OnEnable();
			if (_runMixerState == null)
			{
				_runMixerState = new CartesianMixerState();
				_runMixerState.Add(runRightUp, new Vector2(1f, 1f));
				_runMixerState.Add(runRightDown, new Vector2(1f, -1f));
				_runMixerState.Add(runLeftUp, new Vector2(-1f, 1f));
				_runMixerState.Add(runLeftDown, new Vector2(-1f, -1f));
			}
			if (_idleMixerState == null)
			{
				_idleMixerState = new CartesianMixerState();
				_idleMixerState.Add(idleRightUp, new Vector2(1f, 1f));
				_idleMixerState.Add(idleRightDown, new Vector2(1f, -1f));
				_idleMixerState.Add(idleLeftUp, new Vector2(-1f, 1f));
				_idleMixerState.Add(idleLeftDown, new Vector2(-1f, -1f));
			}
		}

		public override void Run(float x, float y)
		{
			if (!blockAnimations)
			{
				animancer.Layers[0].Play(_runMixerState, 0f);
				float x2 = ((x > 0f) ? 1f : (-1f));
				float y2 = ((y > 0f) ? 1f : (-1f));
				_runMixerState.Parameter = new Vector2(x2, y2);
			}
		}

		public override void Idle(float x, float y)
		{
			if (!blockAnimations)
			{
				animancer.Layers[0].Play(_idleMixerState, 0f);
				float x2 = ((x > 0f) ? 1f : (-1f));
				float y2 = ((y > 0f) ? 1f : (-1f));
				_idleMixerState.Parameter = new Vector2(x2, y2);
			}
		}

		public virtual void Dash(float x, float y)
		{
			if (!blockAnimations)
			{
				if (x > 0f)
				{
					animancer.Layers[0].Play((y > 0f) ? dashRightUp : dashRightDown, 0f);
				}
				else
				{
					animancer.Layers[0].Play((y > 0f) ? dashLeftUp : dashLeftDown, 0f);
				}
			}
		}

		public virtual void Hurt(float x, float y)
		{
			if (!blockAnimations)
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
		}

		public virtual void Dead(float x, float y)
		{
			if (!blockAnimations)
			{
				if (x > 0f)
				{
					animancer.Layers[0].Play((y > 0f) ? deadRightUp : deadRightDown, 0f);
				}
				else
				{
					animancer.Layers[0].Play((y > 0f) ? deadLeftUp : deadLeftDown, 0f);
				}
			}
		}

		public virtual async UniTask Teleport()
		{
			await PlayOverridenAnimations(teleport, 1, resetOnEnd: false, blockOtherAnimations: true);
		}
	}
}
