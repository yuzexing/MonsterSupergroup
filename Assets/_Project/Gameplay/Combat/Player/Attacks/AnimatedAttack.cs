using System;
using System.Collections;
using System.Collections.Generic;
using Animancer;
using AstralShift.Helpers;
using AstralShift.QTI.Helpers.Attributes;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.Serialization;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class AnimatedAttack : BasePlayerAttack
	{
		[Serializable]
		public struct AnimatedAttackSound
		{
			public EventReference eventRef;

			[Tooltip("Gets activated automatically")]
			public bool automatic;

			public AnimatedAttackSound(EventReference eventRef, bool automatic)
			{
				this.eventRef = eventRef;
				this.automatic = automatic;
			}
		}

		[SerializeField]
		protected bool isometricRotation = true;

		[SerializeField]
		[ConditionalHide("isometricRotation", true)]
		protected float isometricAngle = -45f;

		[SerializeField]
		protected bool rotateToDirection;

		[SerializeField]
		protected Transform rotationTransform;

		[SerializeField]
		[Tooltip("True if attack rotates in Y Axis. Default is Z.")]
		private bool rotateInY;

		[SerializeField]
		[Tooltip("Angle offset applied to rotation")]
		private float rotationOffset;

		[SerializeField]
		[Tooltip("Inverts/Negates the angle")]
		private bool invertAngle;

		[Header("Animation Settings")]
		public AnimancerComponent animancer;

		public ClipTransition attackStartAnim;

		public int startAnimLayer;

		public bool attackStartAnimTransitionAfterFinish;

		public ClipTransition attackAnim;

		public int attackAnimLayer;

		[SerializeField]
		private ClipTransition[] additionalAttackAnims;

		public bool attackAnimTransitionAfterFinish;

		public ClipTransition attackEndAnim;

		public int attackEndAnimLayer;

		public ClipTransition attackHitAnim;

		public int hitAnimLayer;

		protected AnimancerState _startAnimState;

		protected AnimancerState _mainAnimState;

		protected AnimancerState _endAnimState;

		private int _animationsToFinish;

		protected Coroutine _timeoutAnimationCoroutine;

		[Header("Sound")]
		[SerializeField]
		[FormerlySerializedAs("attackSound")]
		protected EventReference startSound;

		[SerializeField]
		protected EventReference loopSound;

		[SerializeField]
		protected EventReference endSound;

		[SerializeField]
		protected EventReference hitSound;

		private EventInstance _loopInstance;

		private bool _endSoundPlayed;

		public Transform RotationTransform => rotationTransform;

		protected float _attackAnimDuration { get; set; }

		public Action OnBeforeEnd { get; set; }

		public override void Attack()
		{
			_onStart?.Invoke();
			_onStart = null;
			PlayStartAnimation();
		}

		public override void Dispose()
		{
			StopLoopSound(immediate: true);
		}

		protected virtual void OnDisable()
		{
			StopLoopSound(immediate: true);
		}

		public void Attack(Vector2 direction, bool rotateToDirection = true)
		{
			this.rotateToDirection = rotateToDirection;
			_attackAnimDuration = -1f;
			if (this.rotateToDirection)
			{
				UpdateRotation(direction);
			}
			else
			{
				float x = (isometricRotation ? isometricAngle : 0f);
				base.transform.localEulerAngles = new Vector3(x, base.transform.localEulerAngles.y, base.transform.localEulerAngles.z);
			}
			Attack();
		}

		public virtual void Attack(Vector2 direction, float duration, bool rotateToDirection = true)
		{
			this.rotateToDirection = rotateToDirection;
			_attackAnimDuration = duration;
			if (this.rotateToDirection)
			{
				UpdateRotation(direction);
			}
			else
			{
				float x = (isometricRotation ? isometricAngle : 0f);
				base.transform.localEulerAngles = new Vector3(x, base.transform.localEulerAngles.y, base.transform.localEulerAngles.z);
			}
			Attack();
		}

		public virtual void UpdateRotation(Vector2 direction)
		{
			float x = (isometricRotation ? isometricAngle : 0f);
			float num = (invertAngle ? (0f - Vector2.SignedAngle(Vector2.right, direction)) : Vector2.SignedAngle(Vector2.right, direction));
			if ((bool)rotationTransform)
			{
				rotationTransform.localEulerAngles = new Vector3(x, rotateInY ? (num + rotationOffset) : base.transform.localEulerAngles.y, rotateInY ? base.transform.localEulerAngles.z : (num + rotationOffset));
			}
			else
			{
				base.transform.localEulerAngles = new Vector3(x, rotateInY ? (num + rotationOffset) : base.transform.localEulerAngles.y, rotateInY ? base.transform.localEulerAngles.z : (num + rotationOffset));
			}
		}

		public void PlayStartAnimation()
		{
			_endSoundPlayed = false;
			PlayOneShot(startSound);
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
					currentState.Events(this).OnEnd = null;
				};
			}
		}

		public AnimancerHelpers.WaitForAnimationEnd PlayStartAnimationYield()
		{
			_endSoundPlayed = false;
			PlayOneShot(startSound);
			if (!attackStartAnim.Clip)
			{
				PlayAttackAnimation();
				return new AnimancerHelpers.WaitForAnimationEnd((object)this, (AnimancerState)null);
			}
			AnimancerState currentState = animancer.Layers[startAnimLayer].Play(attackStartAnim, attackStartAnim.FadeDuration);
			currentState.Events(this).OnEnd = delegate
			{
				PlayAttackAnimation();
				currentState.Events(this).OnEnd = null;
			};
			return new AnimancerHelpers.WaitForAnimationEnd(this, new List<AnimancerState> { currentState });
		}

		public virtual void PlayAttackAnimation()
		{
			if (!attackAnim.Clip)
			{
				PlayEndAnimation();
				return;
			}
			StartLoopSound();
			List<AnimancerState> list = new List<AnimancerState>();
			AnimancerState animancerState = animancer.Layers[attackAnimLayer].Play(attackAnim, attackAnim.FadeDuration);
			list.Add(animancerState);
			if (attackAnimTransitionAfterFinish)
			{
				animancerState.Events(this).OnEnd = CheckEndOfAnimations;
			}
			for (int i = 0; i < additionalAttackAnims.Length; i++)
			{
				ClipTransition clipTransition = additionalAttackAnims[i];
				if (clipTransition != null && (bool)clipTransition.Clip)
				{
					AnimancerState animancerState2 = animancer.Layers[attackAnimLayer + i + 1].Play(clipTransition, clipTransition.FadeDuration);
					list.Add(animancerState2);
					animancerState2.Events(this).OnEnd = CheckEndOfAnimations;
				}
			}
			if (list.Count == 0 && attackAnimTransitionAfterFinish)
			{
				_animationsToFinish = list.Count;
				PlayEndAnimation();
				return;
			}
			if (_attackAnimDuration != -1f)
			{
				RunTimeoutAnimation();
			}
			_animationsToFinish = list.Count;
		}

		public AnimancerHelpers.WaitForAnimationEnd PlayAttackAnimationYield()
		{
			if (!attackAnim.Clip)
			{
				PlayEndAnimation();
				return new AnimancerHelpers.WaitForAnimationEnd((object)this, (AnimancerState)null);
			}
			StartLoopSound();
			List<AnimancerState> list = new List<AnimancerState>();
			if ((bool)attackAnim.Clip)
			{
				AnimancerState animancerState = animancer.Layers[attackAnimLayer].Play(attackAnim, attackAnim.FadeDuration);
				list.Add(animancerState);
				if (attackAnimTransitionAfterFinish)
				{
					animancerState.Events(this).OnEnd = PlayEndAnimation;
				}
			}
			for (int i = 0; i < additionalAttackAnims.Length; i++)
			{
				ClipTransition clipTransition = additionalAttackAnims[i];
				if (clipTransition != null && (bool)clipTransition.Clip)
				{
					AnimancerState item = animancer.Layers[attackAnimLayer + i + 1].Play(clipTransition, clipTransition.FadeDuration);
					list.Add(item);
				}
			}
			if (list.Count == 0 && attackAnimTransitionAfterFinish)
			{
				PlayEndAnimation();
			}
			if (_attackAnimDuration != -1f)
			{
				RunTimeoutAnimation();
			}
			_animationsToFinish = list.Count;
			return new AnimancerHelpers.WaitForAnimationEnd(this, list);
		}

		private void CheckEndOfAnimations()
		{
			_animationsToFinish--;
			if (_animationsToFinish <= 0 && attackAnimTransitionAfterFinish)
			{
				if (_timeoutAnimationCoroutine != null)
				{
					StopCoroutine(_timeoutAnimationCoroutine);
					_timeoutAnimationCoroutine = null;
				}
				PlayEndAnimation();
			}
		}

		protected virtual void RunTimeoutAnimation()
		{
			if (_timeoutAnimationCoroutine != null)
			{
				StopCoroutine(_timeoutAnimationCoroutine);
			}
			_timeoutAnimationCoroutine = StartCoroutine(TimeoutAnimation(_attackAnimDuration));
		}

		protected virtual IEnumerator TimeoutAnimation(float duration)
		{
			yield return new WaitForSeconds(duration);
			_timeoutAnimationCoroutine = null;
			PlayEndAnimation();
		}

		public void PlayEndAnimation()
		{
			StopLoopSound();
			if (!_endSoundPlayed)
			{
				_endSoundPlayed = true;
				PlayOneShot(endSound);
			}
			BeforeEndCallback();
			if (!attackEndAnim.Clip)
			{
				EndCallback();
			}
			else
			{
				animancer.Layers[attackEndAnimLayer].Play(attackEndAnim, attackEndAnim.FadeDuration).Events(this).OnEnd = EndCallback;
			}
		}

		public void PlayHitAnimation()
		{
			if ((bool)attackHitAnim.Clip)
			{
				PlayOneShot(hitSound);
				animancer.Layers[hitAnimLayer].Play(attackHitAnim, attackHitAnim.FadeDuration).MoveTime(0f, normalized: true);
			}
		}

		public AnimancerHelpers.WaitForAnimationEnd PlayHitAnimationYield()
		{
			if (!attackHitAnim.Clip)
			{
				return new AnimancerHelpers.WaitForAnimationEnd((object)this, (AnimancerState)null);
			}
			PlayOneShot(hitSound);
			AnimancerState animancerState = animancer.Layers[hitAnimLayer].Play(attackHitAnim, attackHitAnim.FadeDuration);
			animancerState.MoveTime(0f, normalized: true);
			return new AnimancerHelpers.WaitForAnimationEnd(this, animancerState);
		}

		protected virtual void EndCallback()
		{
			_onEnd?.Invoke();
			_onEnd = null;
		}

		protected virtual void BeforeEndCallback()
		{
			OnBeforeEnd?.Invoke();
			OnBeforeEnd = null;
		}

		private void PlayOneShot(EventReference sound)
		{
			if (sound.IsNull)
			{
				return;
			}
			try
			{
				RuntimeManager.PlayOneShotAttached(sound, base.gameObject);
			}
			catch (EventNotFoundException)
			{
				Debug.LogWarning($"FMOD event not found: {sound}", this);
			}
		}

		private void StartLoopSound()
		{
			if (!loopSound.IsNull && !_loopInstance.isValid())
			{
				try
				{
					_loopInstance = RuntimeManager.CreateInstance(loopSound);
				}
				catch (EventNotFoundException)
				{
					Debug.LogWarning($"FMOD event not found: {loopSound}", this);
					return;
				}
				RuntimeManager.AttachInstanceToGameObject(_loopInstance, base.transform);
				_loopInstance.start();
			}
		}

		private void StopLoopSound(bool immediate = false)
		{
			if (_loopInstance.isValid())
			{
				_loopInstance.stop(immediate ? FMOD.Studio.STOP_MODE.IMMEDIATE : FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
				_loopInstance.release();
				_loopInstance.clearHandle();
			}
		}
	}
}
