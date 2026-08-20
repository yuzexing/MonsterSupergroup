using System;
using System.Threading;
using Animancer;
using Assets.Scripts.AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Audio;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.Helpers;
using AstralShift.Managers;
using Cysharp.Threading.Tasks;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks.HoraceAttacks
{
	public class HoraceUltimateAttack : UltimateAttackWeaponBehaviour
	{
		public GameObject attackParent;

		public GameObject chariotParent;

		public GameObject chariotModel;

		public UltimateDamageAttack chariotAttack;

		public Transform chariotPlayerPosition;

		public Rigidbody2D chariotRB;

		private Transform _chariotPhysicsTransform;

		private bool _moving;

		private bool _interrupted;

		private float _steeringDirection;

		private PlayerMovement _player;

		[Header("Movement")]
		public float acceleration = 12f;

		public float maxSpeed = 10f;

		public float reverseSpeed = 5f;

		[Header("Steering")]
		public float turnSpeed = 200f;

		[Header("Attack Trail")]
		public MultiParticlePlayerTrailAttack trailAttackPrefab;

		public Transform trailAttackPivot;

		public float trailDelta = 1f;

		public float trailParticleDuration = 3f;

		private MultiParticlePlayerTrailAttack _currentTrail;

		[Header("Animations")]
		public AnimancerComponent animancerComponent;

		public ClipTransition EntryAnim;

		public ClipTransition BurnoutAnim;

		public ClipTransition MoveLoopAnim;

		public ClipTransition ExitAnim;

		[Header("Trail Sound")]
		[SerializeField]
		protected EventReference trailSound;

		private EventInstance _trailSoundInstance;

		private bool trailSoundWasInterrupted;

		private CancellationTokenSource _cts;

		public bool Started { get; set; }

		private UniTask PlayShowAnimation(CancellationToken token)
		{
			return AnimancerHelpers.AnimationTask(animancerComponent, EntryAnim);
		}

		private UniTask PlayBurnoutAnimation(CancellationToken token)
		{
			return AnimancerHelpers.AnimationTask(animancerComponent, BurnoutAnim);
		}

		private void OnTrailAttackEnd()
		{
			Started = false;
			_moving = false;
			if (_cts == null)
			{
				GameDirector.Instance.Player.SetInvulnerable(state: false);
				return;
			}
			invulTask = PauseAwareDelay(invulnerabilitySafetyDelay, delegate
			{
				GameDirector.Instance.Player.SetInvulnerable(state: false);
			}, _cts.Token);
			if (!_interrupted)
			{
				ControllerManager.Instance.YieldGameController();
			}
			PlayerHand.Instance.ActivateWeapons();
			animancerComponent.Play(ExitAnim);
			if (!trailSound.IsNull)
			{
				_trailSoundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
			}
		}

		private void FixedUpdate()
		{
			if (_moving)
			{
				MovementFixedUpdate();
			}
		}

		private void LateUpdate()
		{
			if (Started && !_interrupted)
			{
				chariotParent.transform.position = new Vector3(_chariotPhysicsTransform.position.x, _chariotPhysicsTransform.position.y, chariotParent.transform.position.z);
				chariotModel.transform.localRotation = Quaternion.Euler(0f, 0f - Vector2.SignedAngle(Vector2.up, _chariotPhysicsTransform.up), 0f);
				_player.transform.position = new Vector3(chariotPlayerPosition.position.x, chariotPlayerPosition.position.y, _player.transform.position.z);
				_player.SetDirection(_chariotPhysicsTransform.up.normalized);
			}
		}

		private void MovementFixedUpdate()
		{
			Move();
			Steer();
			LimitSpeed();
		}

		private void Move()
		{
			Vector2 vector = _chariotPhysicsTransform.up;
			if (chariotRB.linearVelocity.magnitude < maxSpeed)
			{
				chariotRB.AddForce(vector * acceleration);
			}
		}

		private void Steer()
		{
			float num = ((Vector2.Dot(chariotRB.linearVelocity, _chariotPhysicsTransform.up) >= 0f) ? 1 : (-1));
			float num2 = (0f - _steeringDirection) * turnSpeed * num * Time.fixedDeltaTime;
			chariotRB.MoveRotation(chariotRB.rotation + num2);
		}

		private void LimitSpeed()
		{
			if (chariotRB.linearVelocity.magnitude > maxSpeed)
			{
				chariotRB.linearVelocity = chariotRB.linearVelocity.normalized * maxSpeed;
			}
		}

		private async UniTask PauseAwareDelay(float duration, Action onComplete, CancellationToken token)
		{
			float elapsed = 0f;
			while (elapsed < duration && !token.IsCancellationRequested)
			{
				if (!_isPaused)
				{
					elapsed += Time.unscaledDeltaTime;
				}
				await UniTask.Yield(token);
			}
			if (!token.IsCancellationRequested)
			{
				onComplete?.Invoke();
			}
		}

		protected override void Dispose()
		{
			if (attackParent != null)
			{
				UnityEngine.Object.Destroy(attackParent);
			}
		}

		public override void Init()
		{
			base.Init();
			_chariotPhysicsTransform = chariotRB.transform;
			_player = GameDirector.Instance.Player;
			attackParent.transform.parent = null;
			attackParent.SetActive(value: false);
			ref Action onEnd = ref ExitAnim.Events.OnEnd;
			onEnd = (Action)Delegate.Combine(onEnd, (Action)delegate
			{
				attackParent.SetActive(value: false);
			});
			chariotAttack.Init(this);
			if (!trailSound.IsNull)
			{
				_trailSoundInstance = RuntimeManager.CreateInstance(trailSound);
			}
			((IPausable)this).Subscribe();
		}

		public override async void Attack()
		{
			_ = 1;
			try
			{
				attackParent.SetActive(value: true);
				PlayAttackSound();
				_moving = false;
				Started = true;
				_interrupted = false;
				GameDirector.Instance.Player.SetInvulnerable(state: true);
				ControllerManager.Instance.OverrideGameController<HoraceUltimateController>();
				PlayerHand.Instance.DeactivateWeapons();
				_chariotPhysicsTransform.position = player.CurrentPosition;
				_cts = new CancellationTokenSource();
				await PlayShowAnimation(_cts.Token);
				if (_interrupted)
				{
					return;
				}
				_currentTrail = UnityEngine.Object.Instantiate(trailAttackPrefab);
				_currentTrail.trailStart = trailAttackPivot;
				_currentTrail.SetTrailDelta(trailDelta);
				_currentTrail.SetTrailParticleDuration(trailParticleDuration);
				_currentTrail.Init(this);
				MultiParticlePlayerTrailAttack currentTrail = _currentTrail;
				currentTrail.onAttackDurationFinished = (Action)Delegate.Combine(currentTrail.onAttackDurationFinished, new Action(OnTrailAttackEnd));
				_currentTrail.Attack();
				_moving = true;
				await PlayBurnoutAnimation(_cts.Token);
				if (!_interrupted)
				{
					KnockbackEnemies();
					animancerComponent.Play(MoveLoopAnim);
					if (!trailSound.IsNull)
					{
						_trailSoundInstance.start();
					}
				}
			}
			catch (Exception message)
			{
				Debug.LogError(message);
			}
		}

		public override void OnPausePausables()
		{
			animancerComponent.UpdateMode = AnimatorUpdateMode.Normal;
			_isPaused = true;
			Interrupt();
		}

		public override void OnResumePausables()
		{
			animancerComponent.UpdateMode = AnimatorUpdateMode.UnscaledTime;
			_isPaused = false;
			if (trailSoundWasInterrupted)
			{
				_trailSoundInstance.start();
			}
		}

		public override void OnGamePause()
		{
			animancerComponent.UpdateMode = AnimatorUpdateMode.Normal;
			_isPaused = true;
		}

		public override void OnGameResume()
		{
			animancerComponent.UpdateMode = AnimatorUpdateMode.UnscaledTime;
			_isPaused = false;
		}

		public override void Interrupt()
		{
			if (!_interrupted)
			{
				if ((bool)_currentTrail)
				{
					_currentTrail.Interrupt();
				}
				_interrupted = true;
				_cts?.Cancel();
				_cts?.Dispose();
				_cts = null;
				attackParent.SetActive(value: false);
			}
			if (!trailSound.IsNull && _trailSoundInstance.isValid())
			{
				_trailSoundInstance.getPlaybackState(out var state);
				trailSoundWasInterrupted = state == PLAYBACK_STATE.PLAYING;
				_trailSoundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
				_trailSoundInstance.release();
			}
		}

		public void ReturnFromInterrupt()
		{
			_interrupted = false;
			if ((bool)_currentTrail)
			{
				_currentTrail.ReturnFromInterrupt();
			}
			MusicPlayer.Instance.SetSnapShot(MusicPlayer.SnapshotID.Normal);
		}

		internal void SetHorizontalDirection(Vector2 movementDirection)
		{
			_steeringDirection = movementDirection.x;
		}

		private void OnDisable()
		{
			if (!trailSound.IsNull && _trailSoundInstance.isValid())
			{
				_trailSoundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
				_trailSoundInstance.release();
			}
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = null;
			((IPausable)this).UnSubscribe();
		}

		private void OnDestroy()
		{
			Dispose();
		}
	}
}
