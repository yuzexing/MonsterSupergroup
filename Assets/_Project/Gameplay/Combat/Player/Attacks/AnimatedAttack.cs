using System;
using System.Collections;
using System.Collections.Generic;
using Animancer;
using AstralShift.Helpers;
using AstralShift.QTI.Helpers.Attributes;
using FMOD.Studio;
using FMODUnity;
using MonsterSupergroup.GAS;
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

		private readonly List<AnimancerState> _ownedAnimationStates = new List<AnimancerState>();

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

		/// <summary>External deactivation must close presentation tracking before releasing the attack's final lease.</summary>
		public event Action<AnimatedAttack> Deactivated;

		public override void InitNative(WeaponBehaviour behaviour, AttackSnapshot attack,
			Action onStart = null, Action onEnd = null)
		{
			Dispose();
			base.InitNative(behaviour, attack, onStart, onEnd);
		}

		public override void InitPresentation(WeaponBehaviour behaviour, ProjectilePresentationStats stats,
			Action onStart = null, Action onEnd = null)
		{
			Dispose();
			base.InitPresentation(behaviour, stats, onStart, onEnd);
		}

		public override void Attack()
		{
			_onStart?.Invoke();
			_onStart = null;
			PlayStartAnimation();
		}

		public override void Dispose()
		{
			Deactivated = null;
			if (_timeoutAnimationCoroutine != null)
			{
				StopCoroutine(_timeoutAnimationCoroutine);
				_timeoutAnimationCoroutine = null;
			}
			foreach (AnimancerState state in _ownedAnimationStates)
				if (state.IsValid()) state.Events(this).OnEnd = null;
			_ownedAnimationStates.Clear();
			if (animancer != null) animancer.Stop();
			_startAnimState = _mainAnimState = _endAnimState = null;
			_animationsToFinish = 0;
			_onStart = null;
			_onEnd = null;
			OnBeforeEnd = null;
			if (hitbox != null) hitbox.ClearCallbacks();
			ReleaseNativeAttackSnapshot();
			_behaviour = null;
			StopLoopSound(immediate: true);
		}

		protected virtual void OnDisable()
		{
			Action<AnimatedAttack> deactivated = Deactivated;
			Deactivated = null;
			try { deactivated?.Invoke(this); }
			finally { Dispose(); }
		}

		private void OnDestroy() => Dispose();

		private AnimancerState TrackAnimation(AnimancerState state)
		{
			if (!_ownedAnimationStates.Contains(state)) _ownedAnimationStates.Add(state);
			return state;
		}

		public float GetPresentationDuration(float duration)
		{
			return ClipDuration(attackStartAnim) + MainPresentationDuration(duration) + ClipDuration(attackEndAnim);
		}

		private float MainPresentationDuration(float duration)
		{
			float natural = ClipDuration(attackAnim);
			foreach (ClipTransition additional in additionalAttackAnims ?? Array.Empty<ClipTransition>())
				natural = Mathf.Max(natural, ClipDuration(additional));
			if (duration >= 0f && attackAnim != null && attackAnim.Clip != null)
				return attackAnimTransitionAfterFinish ? Mathf.Min(natural, duration) : duration;
			return natural;
		}

		private static float ClipDuration(ClipTransition transition)
		{
			if (transition == null || transition.Clip == null) return 0f;
			float speed = Mathf.Abs(transition.Speed);
			return Mathf.Abs(transition.Length) / (float.IsNaN(speed) ? 1f : Mathf.Max(0.0001f, speed));
		}

		public float GetEndPresentationDuration() => ClipDuration(attackEndAnim);

		/// <summary>Play an externally timed sequence without changing the authored root rotation.</summary>
		public void PlayExternallyTimedAnimation(float elapsedSeconds = 0f)
		{
			ValidateAnimationAge(elapsedSeconds);
			_attackAnimDuration = -1f;
			Attack();
			if (elapsedSeconds <= 0f || !gameObject.activeInHierarchy) return;
			float startDuration = ClipDuration(attackStartAnim);
			if (elapsedSeconds < startDuration)
			{
				SeekStateFromStart(_startAnimState, elapsedSeconds);
				return;
			}
			if (startDuration > 0f)
			{
				if (_startAnimState.IsValid()) _startAnimState.Events(this).OnEnd = null;
				PlayAttackAnimation();
			}
			foreach (AnimancerState state in _ownedAnimationStates)
				if (state != _startAnimState && state != _endAnimState)
					SeekStateFromStart(state, elapsedSeconds - startDuration);
		}

		/// <summary>Start or correct an externally timed ending, without adding its time to gameplay cooldown.</summary>
		public void PlayExternallyTimedEnd(float elapsedSeconds = 0f)
		{
			ValidateAnimationAge(elapsedSeconds);
			PlayEndAnimation();
			if (!gameObject.activeInHierarchy || !_endAnimState.IsValid()) return;
			SeekStateFromStart(_endAnimState, elapsedSeconds);
			if (elapsedSeconds >= GetEndPresentationDuration())
			{
				_endAnimState.Events(this).OnEnd = null;
				EndCallback();
			}
		}

		private static void ValidateAnimationAge(float elapsedSeconds)
		{
			if (float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds) || elapsedSeconds < 0f)
				throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
		}

		private static void SeekStateFromStart(AnimancerState state, float elapsedSeconds)
		{
			if (state.IsValid()) state.Time = elapsedSeconds * state.Speed;
		}

		/// <summary>Seek an already aged visual without creating or retaining a gameplay attack.</summary>
		public void PlayPresentation(Vector2 direction, float duration, float elapsedSeconds)
		{
			if (!IsPresentationOnly)
				throw new InvalidOperationException("Melee presentation requires InitPresentation first.");
			if (duration >= 0f) Attack(direction, duration);
			else Attack(direction);
			if (elapsedSeconds <= 0f || !gameObject.activeInHierarchy) return;

			float startDuration = ClipDuration(attackStartAnim);
			float mainDuration = MainPresentationDuration(duration);
			if (elapsedSeconds < startDuration)
			{
				SeekState(_startAnimState, elapsedSeconds);
				return;
			}
			if (elapsedSeconds >= startDuration + mainDuration)
			{
				PlayEndAnimation();
				SeekState(_endAnimState, elapsedSeconds - startDuration - mainDuration);
				return;
			}
			if (startDuration > 0f)
			{
				if (_startAnimState.IsValid()) _startAnimState.Events(this).OnEnd = null;
				PlayAttackAnimation();
			}
			foreach (AnimancerState state in _ownedAnimationStates)
				if (state != _startAnimState) SeekState(state, elapsedSeconds - startDuration);
			if (_timeoutAnimationCoroutine != null)
			{
				StopCoroutine(_timeoutAnimationCoroutine);
				_timeoutAnimationCoroutine = StartCoroutine(TimeoutAnimation(
					Mathf.Max(0f, duration - elapsedSeconds + startDuration)));
			}
		}

		private static void SeekState(AnimancerState state, float elapsedSeconds)
		{
			if (state != null && state.IsValid()) state.Time += elapsedSeconds * state.Speed;
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
			if (attackStartAnim == null || !attackStartAnim.Clip || animancer == null)
			{
				PlayAttackAnimation();
				return;
			}
			AnimancerState currentState = _startAnimState = TrackAnimation(animancer.Layers[startAnimLayer].Play(attackStartAnim, attackStartAnim.FadeDuration));
			if (attackStartAnimTransitionAfterFinish)
			{
				currentState.Events(this).OnEnd = delegate
				{
					currentState.Events(this).OnEnd = null;
					PlayAttackAnimation();
				};
			}
		}

		public AnimancerHelpers.WaitForAnimationEnd PlayStartAnimationYield()
		{
			_endSoundPlayed = false;
			PlayOneShot(startSound);
			if (attackStartAnim == null || !attackStartAnim.Clip || animancer == null)
			{
				PlayAttackAnimation();
				return new AnimancerHelpers.WaitForAnimationEnd((object)this, (AnimancerState)null);
			}
			AnimancerState currentState = _startAnimState = TrackAnimation(animancer.Layers[startAnimLayer].Play(attackStartAnim, attackStartAnim.FadeDuration));
			currentState.Events(this).OnEnd = delegate
			{
				currentState.Events(this).OnEnd = null;
				PlayAttackAnimation();
			};
			return new AnimancerHelpers.WaitForAnimationEnd(this, new List<AnimancerState> { currentState });
		}

		public virtual void PlayAttackAnimation()
		{
			if (attackAnim == null || !attackAnim.Clip || animancer == null)
			{
				PlayEndAnimation();
				return;
			}
			StartLoopSound();
			List<AnimancerState> list = new List<AnimancerState>();
			AnimancerState animancerState = _mainAnimState = TrackAnimation(animancer.Layers[attackAnimLayer].Play(attackAnim, attackAnim.FadeDuration));
			list.Add(animancerState);
			if (attackAnimTransitionAfterFinish)
			{
				animancerState.Events(this).OnEnd = CheckEndOfAnimations;
			}
			for (int i = 0; i < (additionalAttackAnims?.Length ?? 0); i++)
			{
				ClipTransition clipTransition = additionalAttackAnims[i];
				if (clipTransition != null && (bool)clipTransition.Clip)
				{
					AnimancerState animancerState2 = TrackAnimation(animancer.Layers[attackAnimLayer + i + 1].Play(clipTransition, clipTransition.FadeDuration));
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
			if (attackAnim == null || !attackAnim.Clip || animancer == null)
			{
				PlayEndAnimation();
				return new AnimancerHelpers.WaitForAnimationEnd((object)this, (AnimancerState)null);
			}
			StartLoopSound();
			List<AnimancerState> list = new List<AnimancerState>();
			if ((bool)attackAnim.Clip)
			{
				AnimancerState animancerState = _mainAnimState = TrackAnimation(animancer.Layers[attackAnimLayer].Play(attackAnim, attackAnim.FadeDuration));
				list.Add(animancerState);
				if (attackAnimTransitionAfterFinish)
				{
					animancerState.Events(this).OnEnd = PlayEndAnimation;
				}
			}
			for (int i = 0; i < (additionalAttackAnims?.Length ?? 0); i++)
			{
				ClipTransition clipTransition = additionalAttackAnims[i];
				if (clipTransition != null && (bool)clipTransition.Clip)
				{
					AnimancerState item = TrackAnimation(animancer.Layers[attackAnimLayer + i + 1].Play(clipTransition, clipTransition.FadeDuration));
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
			if (_timeoutAnimationCoroutine != null)
			{
				StopCoroutine(_timeoutAnimationCoroutine);
				_timeoutAnimationCoroutine = null;
			}
			foreach (AnimancerState state in _ownedAnimationStates)
				if (state.IsValid()) state.Events(this).OnEnd = null;
			StopLoopSound();
			if (!_endSoundPlayed)
			{
				_endSoundPlayed = true;
				PlayOneShot(endSound);
			}
			BeforeEndCallback();
			if (attackEndAnim == null || !attackEndAnim.Clip || animancer == null)
			{
				EndCallback();
			}
			else
			{
				_endAnimState = TrackAnimation(animancer.Layers[attackEndAnimLayer].Play(attackEndAnim, attackEndAnim.FadeDuration));
				_endAnimState.Events(this).OnEnd = EndCallback;
			}
		}

		public void PlayHitAnimation()
		{
			if (attackHitAnim != null && (bool)attackHitAnim.Clip && animancer != null)
			{
				PlayOneShot(hitSound);
				TrackAnimation(animancer.Layers[hitAnimLayer].Play(attackHitAnim, attackHitAnim.FadeDuration)).MoveTime(0f, normalized: true);
			}
		}

		public AnimancerHelpers.WaitForAnimationEnd PlayHitAnimationYield()
		{
			if (attackHitAnim == null || !attackHitAnim.Clip || animancer == null)
			{
				return new AnimancerHelpers.WaitForAnimationEnd((object)this, (AnimancerState)null);
			}
			PlayOneShot(hitSound);
			AnimancerState animancerState = TrackAnimation(animancer.Layers[hitAnimLayer].Play(attackHitAnim, attackHitAnim.FadeDuration));
			animancerState.MoveTime(0f, normalized: true);
			return new AnimancerHelpers.WaitForAnimationEnd(this, animancerState);
		}

		protected virtual void EndCallback()
		{
			Action onEnd = _onEnd;
			_onEnd = null;
			onEnd?.Invoke();
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
				OptionalAudio.PlayOneShotAttached(sound, base.gameObject);
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
					_loopInstance = OptionalAudio.CreateInstance(loopSound);
				}
				catch (EventNotFoundException)
				{
					Debug.LogWarning($"FMOD event not found: {loopSound}", this);
					return;
				}
				OptionalAudio.AttachInstanceToGameObject(_loopInstance, base.transform);
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
