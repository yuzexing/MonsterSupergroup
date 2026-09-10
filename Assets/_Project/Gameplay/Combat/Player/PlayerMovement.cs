using System;
using System.Collections;
using System.Collections.Generic;
using Assets.Scripts.AstralShift.HellMaiden.Controllers;
using AstralShift.FSM;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.Characters;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.HellMaiden.Scenes;
using AstralShift.Helpers;
using AstralShift.Managers;
using AstralShift.QTI.Interactors;
using Com.LuisPedroFonseca.ProCamera2D;
using FMODUnity;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace AstralShift.HellMaiden.Player
{
	public class PlayerMovement : CharacterMovement, ILootColector
	{
		[SerializeField]
		private Collider2D _hitboxCollider;

		[SerializeField]
		private PlayerVFX playerVFX;

		[SerializeField]
		private CircleCollider2D _obstacleCollider;

		public PlayerAnimator playerAnimator;

		[SerializeField]
		private PlayerEffectResolver playerEffectResolver;

		public Transform AttacksParent;

		[SerializeField]
		private Transform enemyAttackTarget;

		public Leveler leveler;

		public Interaction2DFinder interactionFinder;

		[SerializeField]
		private Interactor interactor;

		public AutoAim autoAim;

		public UltimateAttackManager ultimateAttackManager;

		[SerializeField]
		private PlayerStats playerStats;

		[SerializeField]
		private PlayerCombatantBinding combatantBinding;

		[SerializeField]
		private float hurtTime = 0.3f;

		[SerializeField]
		private float invulnerabilityTime = 0.6f;

		[SerializeField]
		private EventReference dashAudio;

		[SerializeField]
		private EventReference hurtSound;

		[SerializeField]
		private EventReference deadSound;

		[SerializeField]
		private EventReference teleportSound;

		[SerializeField]
		private LayerMask dashExclusionLayerMask;

		[SerializeField]
		private AnimationCurve dashCurve;

		[SerializeField]
		private float dashObstacleMargin = 2.5f;

		[SerializeField]
		public LayerMask obstacleLayerMask;

		[SerializeField]
		public LayerMask edgeLayerMask;

		[SerializeField]
		private float dashBufferTime = 0.1f;

		private LayerMask _defaultHitboxLayerMask;

		private LayerMask _defaultObstacleLayerMask;

		private Vector2 _previousInputDirection = Vector2.down;

		private bool _autoAim;

		private Camera _inputCamera;

		private Vector2 _dashDirection = Vector2.zero;

		private float _dashElapsedTime;

		private float _totalDashTime;

		private float _currentDashDistance;

		private bool _isDashInitialized;

		private ActionBuffer _dashBuffer;

		private PlayerDashRuntime _dashRuntime;
		private Func<double> _dashClock;
		private Func<DashMotionParameters, ulong> _tryCommitDashUse;
		private PlayerStats _dashSubscribedStats;
		private CombatantBehaviour _dashSubscribedCombatant;
		private float _dashPeakSpeed;
		private bool _dashUseStarted;
		private bool _cancellingDash;
		private uint _dashGeneration;

		private List<Coroutine> _invulnerabilityCoroutines = new List<Coroutine>();

		private float _hurtTime = 0.3f;

		private int _damageReceived;

		private float _currentStunDuration;

		private int _invulnerabilityCount;

		protected State Dashing;

		protected State Hurt;

		protected State GivingUp;

		protected State Dead;

		protected State Stunned;

		protected State Knockback;

		private Vector2 _startPoint;

		private Vector2 _endPoint;

		private Vector2 _lastPosition;

		private float _maxKnockbackTime = 1f;

		private float _knockBackTime;

		private float _elapsedTime;

		private KnockbackSettings currentKnockbackSettings;

		private readonly int HitEffectColorSID = Shader.PropertyToID("_HitEffectColor");

		private readonly int HitEffectBlendSID = Shader.PropertyToID("_HitEffectBlend");

		private Coroutine _damageColorAnimation;

		private readonly WaitForSeconds _damageColorAnimationWait = new WaitForSeconds(0.04f);

		private MaterialPropertyBlock _damagePropertyBlock;

		private bool _ultimateCharge;
		private Func<bool> _tryUseNativeUltimate;
		private Func<bool> _hasNativeUltimateCharge;

		public PlayerEffectResolver EffectVisualsResolver => playerEffectResolver;

		public Transform EnemyAttackTarget => enemyAttackTarget;

		public PlayerStats PlayerStats => playerStats;

		public PlayerCombatantBinding CombatantBinding => combatantBinding;

		public Vector2 attackDirection { get; private set; } = Vector2.right;

		public Vector2 DashDirection => _dashDirection;

		public float TotalDashTime => _totalDashTime;

		public float DashDistance => _currentDashDistance;

		public PlayerDashRuntime OwnerDashRuntime => _dashRuntime;

		public ulong CurrentDashUseId { get; private set; }

		public override float MoveSpeed => PlayerStats.currentStats.moveSpeed;

		public Vector2 DebuffForce { get; set; } = Vector2.zero;

		public Vector2 WindForce { get; set; } = Vector2.zero;

		private bool _isInvulnerable => _invulnerabilityCount > 0;

		private RigidbodyConstraints2D _constraintsBeforeUpgradeSelection;

		public bool IsUpgradeSelectionLocked { get; private set; }

		public void SetUpgradeSelectionLocked(bool value)
		{
			combatantBinding?.Combatant?.SetUpgradeSelectionInvulnerable(value);
			if (IsUpgradeSelectionLocked == value)
			{
				return;
			}

			IsUpgradeSelectionLocked = value;
			if (value) CancelDash();
			_currentInputDirection = Vector2.zero;
			if (body != null)
			{
				body.linearVelocity = Vector2.zero;
				body.angularVelocity = 0f;
				if (value)
				{
					_constraintsBeforeUpgradeSelection = body.constraints;
					body.constraints |= RigidbodyConstraints2D.FreezePosition;
				}
				else
				{
					body.constraints = _constraintsBeforeUpgradeSelection;
				}
			}
		}

		public bool IsInvulnerable
		{
			get
			{
				if (!DebugIsInvulnerable)
				{
					return _isInvulnerable || IsUpgradeSelectionLocked || (combatantBinding?.Combatant?.IsInvulnerable ?? false);
				}
				return true;
			}
		}

		public bool DebugIsInvulnerable { get; private set; }

		public bool HasUltimateCharge => _hasNativeUltimateCharge?.Invoke() ?? (!UsesNetworkLifecycle && _ultimateCharge);

		public void BindUltimateInput(Func<bool> tryUse, Func<bool> hasCharge)
		{
			_tryUseNativeUltimate = tryUse ?? throw new System.ArgumentNullException(nameof(tryUse));
			_hasNativeUltimateCharge = hasCharge ?? throw new System.ArgumentNullException(nameof(hasCharge));
		}

		public void UnbindUltimateInput()
		{
			_tryUseNativeUltimate = null;
			_hasNativeUltimateCharge = null;
		}

		public event Action OnDashStart;

		public event Action OnDashEnd;

		public bool IsRuntimeInitialized { get; private set; }

		public bool UsesNetworkLifecycle { get; private set; }

		public bool IsLocalOwnerBound { get; private set; }

		public event Action Died;

		public event Action DeathPresentationCompleted;

		public void ConfigureNetworkLifecycle() => UsesNetworkLifecycle = true;

		public void SetLocalOwnerBound(bool value)
		{
			IsLocalOwnerBound = value;
			if (!value)
			{
				CancelDash();
				ResetInputDirection();
				DisableInteractor();
			}
		}

		/// <summary>Explicit per-avatar initialization. Safe before Start and on repeated callbacks.</summary>
		public void EnsureRuntimeInitialized()
		{
			if (IsRuntimeInitialized) return;
			if (playerStats == null)
				throw new InvalidOperationException("PlayerMovement requires PlayerStats.");
			PlayerStats.Init();
			if (combatantBinding == null)
				combatantBinding = GetComponent<PlayerCombatantBinding>();
			if (combatantBinding == null)
				throw new InvalidOperationException("PlayerMovement requires PlayerCombatantBinding.");
			combatantBinding.InitializeFromPlayerStats();
			_dashBuffer = new ActionBuffer(dashBufferTime);
			if (_hitboxCollider != null) _defaultHitboxLayerMask = _hitboxCollider.excludeLayers;
			if (_obstacleCollider != null) _defaultObstacleLayerMask = _obstacleCollider.excludeLayers;
			IsRuntimeInitialized = true;
			SubscribeDashSources();
		}

		public void BindDashRuntime(PlayerDashRuntime runtime, Func<double> now,
			Func<DashMotionParameters, ulong> tryCommitUse)
		{
			if (runtime == null) throw new System.ArgumentNullException(nameof(runtime));
			if (now == null) throw new System.ArgumentNullException(nameof(now));
			if (tryCommitUse == null) throw new System.ArgumentNullException(nameof(tryCommitUse));
			if (!ReferenceEquals(_dashRuntime, runtime)) UnbindDashRuntime();
			_dashRuntime = runtime;
			_dashClock = now;
			_tryCommitDashUse = tryCommitUse;
			SubscribeDashSources();
			RefreshDashRuntime(out _);
		}

		public void UnbindDashRuntime()
		{
			CancelDash();
			UnsubscribeDashSources();
			_dashRuntime = null;
			_dashClock = null;
			_tryCommitDashUse = null;
		}

		private void SubscribeDashSources()
		{
			UnsubscribeDashSources();
			if (_dashRuntime == null || !IsRuntimeInitialized) return;
			_dashSubscribedStats = playerStats;
			if (_dashSubscribedStats != null)
			{
				_dashSubscribedStats.MaximumDashesChanged += HandleMaximumDashesChanged;
				HandleMaximumDashesChanged(_dashSubscribedStats.currentStats.maxDashCharges);
			}
			_dashSubscribedCombatant = combatantBinding != null ? combatantBinding.Combatant : null;
			if (_dashSubscribedCombatant != null) _dashSubscribedCombatant.HealthChanged += HandleDashHealthChanged;
		}

		private void UnsubscribeDashSources()
		{
			if (_dashSubscribedStats != null) _dashSubscribedStats.MaximumDashesChanged -= HandleMaximumDashesChanged;
			if (_dashSubscribedCombatant != null) _dashSubscribedCombatant.HealthChanged -= HandleDashHealthChanged;
			_dashSubscribedStats = null;
			_dashSubscribedCombatant = null;
		}

		private void HandleMaximumDashesChanged(int maximum)
		{
			_dashRuntime?.ConfigureCapacity(Math.Max(0, maximum));
			SyncDashChargeMirror();
		}

		private void HandleDashHealthChanged(int current, int maximum)
		{
			if (current <= 0) CancelDash();
		}

		private bool RefreshDashRuntime(out double now)
		{
			now = 0d;
			if (_dashRuntime == null || _dashClock == null) return false;
			now = _dashClock();
			if (double.IsNaN(now) || double.IsInfinity(now) || now < 0d) return false;
			_dashRuntime.Refresh(now);
			SyncDashChargeMirror();
			return true;
		}

		private void SyncDashChargeMirror()
		{
			if (_dashRuntime != null && playerStats != null)
				playerStats.currentStats.dashCharges = _dashRuntime.AvailableCharges;
		}

		private bool CanExecuteDash => isActiveAndEnabled && IsRuntimeInitialized && !_cancellingDash &&
			_dashRuntime != null && _tryCommitDashUse != null && !IsUpgradeSelectionLocked &&
			combatantBinding != null && combatantBinding.IsAlive && (!UsesNetworkLifecycle || IsLocalOwnerBound);

		private bool CanCommitDash => CanExecuteDash && RefreshDashRuntime(out double now) &&
			_dashRuntime.AvailableCharges > 0 && now >= _dashRuntime.NextUseAt;

		public void CancelDash(ulong expectedUseId = 0)
		{
			if (_cancellingDash || (expectedUseId != 0 && expectedUseId != CurrentDashUseId)) return;
			_cancellingDash = true;
			try
			{
				_dashGeneration++;
				_dashBuffer.Consume();
				if (_stateMachine != null && _stateMachine.GetState() == Dashing)
					_stateMachine.MakeTransition(Moving);
				else FinishDashUse();
				_isDashInitialized = false;
				_dashElapsedTime = 0f;
				if (body != null) body.linearVelocity = Vector2.zero;
			}
			finally { _cancellingDash = false; }
		}

		private void FinishDashUse()
		{
			SetDefaultLayerMask();
			bool started = _dashUseStarted;
			_dashUseStarted = false;
			try { if (started) OnDashEnd?.Invoke(); }
			finally { CurrentDashUseId = 0; }
		}

		public override void Awake()
		{
			if (combatantBinding == null)
			{
				combatantBinding = GetComponent<PlayerCombatantBinding>();
				if (combatantBinding == null)
				{
					combatantBinding = gameObject.AddComponent<PlayerCombatantBinding>();
				}
			}

			_stateMachine = new StateMachine("PlayerMovement");
			Moving = new State("Moving");
			Dashing = new State("Dashing");
			Hurt = new State("Hurt");
			Dead = new State("Dead");
			Stunned = new State("Stunned");
			GivingUp = new State("GivingUp");
			Knockback = new State("Knockback");
			_stateMachine.AddTransition(Moving, Dashing);
			_stateMachine.AddTransition(Moving, Hurt);
			_stateMachine.AddTransition(Moving, Stunned);
			_stateMachine.AddTransition(Dashing, Moving);
			_stateMachine.AddTransition(Hurt, Moving);
			_stateMachine.AddTransition(Hurt, Dashing);
			_stateMachine.AddTransition(Hurt, Dead);
			_stateMachine.AddTransition(Stunned, Moving);
			_stateMachine.AddTransition(Stunned, Dead);
			_stateMachine.AddTransition(Hurt, Stunned);
			_stateMachine.AddTransition(Dead, Moving);
			_stateMachine.AddAnyTransition(GivingUp);
			_stateMachine.AddTransition(Moving, Knockback);
			_stateMachine.AddTransition(Hurt, Knockback);
			_stateMachine.AddTransition(GivingUp, Dead);
			_stateMachine.AddTransition(Knockback, Moving);
			State moving = Moving;
			moving.onFixedUpdateTick = (Action)Delegate.Combine(moving.onFixedUpdateTick, new Action(OnFixedUpdateMoving));
			State moving2 = Moving;
			moving2.onLateUpdateTick = (Action)Delegate.Combine(moving2.onLateUpdateTick, new Action(OnLateUpdateMoving));
			State dashing = Dashing;
			dashing.onEnter = (Action)Delegate.Combine(dashing.onEnter, new Action(OnEnterDashing));
			State dashing2 = Dashing;
			dashing2.onFixedUpdateTick = (Action)Delegate.Combine(dashing2.onFixedUpdateTick, new Action(OnFixedUpdateDashing));
			State dashing3 = Dashing;
			dashing3.onLateUpdateTick = (Action)Delegate.Combine(dashing3.onLateUpdateTick, new Action(OnLateUpdateDashing));
			State dashing4 = Dashing;
			dashing4.onExit = (Action)Delegate.Combine(dashing4.onExit, new Action(OnExitDashing));
			State hurt = Hurt;
			hurt.onEnter = (Action)Delegate.Combine(hurt.onEnter, new Action(OnEnterHurt));
			State hurt2 = Hurt;
			hurt2.onUpdateTick = (Action)Delegate.Combine(hurt2.onUpdateTick, new Action(OnUpdateHurt));
			State givingUp = GivingUp;
			givingUp.onEnter = (Action)Delegate.Combine(givingUp.onEnter, new Action(OnEnterGivingUp));
			State dead = Dead;
			dead.onEnter = (Action)Delegate.Combine(dead.onEnter, new Action(OnEnterDead));
			State dead2 = Dead;
			dead2.onExit = (Action)Delegate.Combine(dead2.onExit, new Action(OnExitDead));
			State stunned = Stunned;
			stunned.onEnter = (Action)Delegate.Combine(stunned.onEnter, new Action(OnEnterStun));
			State stunned2 = Stunned;
			stunned2.onUpdateTick = (Action)Delegate.Combine(stunned2.onUpdateTick, new Action(OnStunTick));
			State knockback = Knockback;
			knockback.onFixedUpdateTick = (Action)Delegate.Combine(knockback.onFixedUpdateTick, new Action(OnFixedUpdateKnockBack));
			_stateMachine.SetInitialState(Moving);
		}

		protected override void Start()
		{
			// Runtime creation belongs to the spawn/load boundary, never to Start.
		}

		protected override void OnDestroy()
		{
			UnbindDashRuntime();
			UnbindUltimateInput();
			SetUpgradeSelectionLocked(false);
			UnSubscribeSceneEvents();
			if (autoAim != null)
			{
				autoAim.OnTargetUpdate = (Action)Delegate.Remove(autoAim.OnTargetUpdate, new Action(OnAutoAimUpdate));
			}
		}

		public void RestartStats()
		{
			if (UsesNetworkLifecycle)
				throw new InvalidOperationException("Network player stats must be restored through the player runtime lifecycle.");
			PlayerStats.Init();
			combatantBinding.InitializeFromPlayerStats();
			IsRuntimeInitialized = true;
		}

		public void RestartPlayer()
		{
			animator.ResetAnimancer();
			_stateMachine.MakeTransition(Moving);
			base.gameObject.SetActive(value: true);
			EnableInteractor();
			RemoveExternalForces();
		}

		private void RemoveExternalForces()
		{
			WindForce = Vector2.zero;
			DebuffForce = Vector2.zero;
		}

		private void SubscribeSceneEvents()
		{
			SceneMaster.Instance.OnSceneHideStartPersist += delegate
			{
				ResetInvulnerability();
				SetInvulnerable(state: true);
			};
			SceneMaster.Instance.OnSceneShowFinishPersist += delegate
			{
				SetInvulnerable(state: false);
			};
			((IPausable)this).Subscribe();
		}

		private void ResetInvulnerability()
		{
			for (int i = 0; i < _invulnerabilityCoroutines.Count; i++)
			{
				StopCoroutine(_invulnerabilityCoroutines[i]);
			}
			_invulnerabilityCoroutines.Clear();
			_invulnerabilityCount = 0;
		}

		private void UnSubscribeSceneEvents()
		{
			((IPausable)this).UnSubscribe();
		}

		public void SubscribeGameEvents()
		{
			GameEvents instance = GameEvents.Instance;
			instance.OnHealthDecrease = (Action<int>)Delegate.Combine(instance.OnHealthDecrease, new Action<int>(CameraEffects.Instance.Health));
			SceneMaster.Instance.OnSceneHideStart += UnSubscribeGameEvents;
		}

		private void UnSubscribeGameEvents()
		{
			GameEvents instance = GameEvents.Instance;
			instance.OnHealthDecrease = (Action<int>)Delegate.Remove(instance.OnHealthDecrease, new Action<int>(CameraEffects.Instance.Health));
		}

		public void SubscribeAutoAim()
		{
			_autoAim = GameDirector.Instance.Settings.AutoAim;
			if (_autoAim)
			{
				autoAim.Enable();
			}
			GameDirector.Instance.Settings.OnAutoAimChange += OnAutoAimOptionChanged;
			AutoAim obj = autoAim;
			obj.OnTargetUpdate = (Action)Delegate.Combine(obj.OnTargetUpdate, new Action(OnAutoAimUpdate));
		}

		public void UnSubscribeAutoAim()
		{
			autoAim.Disable();
			GameDirector.Instance.Settings.OnAutoAimChange -= OnAutoAimOptionChanged;
			AutoAim obj = autoAim;
			obj.OnTargetUpdate = (Action)Delegate.Remove(obj.OnTargetUpdate, new Action(OnAutoAimUpdate));
		}

		private void Update()
		{
			RefreshDashRuntime(out _);
			if (_stateMachine != null && _stateMachine.GetState() == Dashing && !CanExecuteDash) CancelDash();
			if (!IsRuntimeInitialized || IsUpgradeSelectionLocked) return;
			_stateMachine.UpdateTick();
		}

		private void FixedUpdate()
		{
			if (_stateMachine != null && _stateMachine.GetState() == Dashing && !CanExecuteDash) CancelDash();
			if (!IsRuntimeInitialized) return;
			if (IsUpgradeSelectionLocked)
			{
				if (body != null) StopMovement();
				return;
			}
			_stateMachine.FixedUpdateTick();
		}

		private void LateUpdate()
		{
			if (!IsRuntimeInitialized || IsUpgradeSelectionLocked) return;
			_stateMachine.LateUpdateTick();
		}

		public override void SetDirection(Vector2 value)
		{
			if (IsUpgradeSelectionLocked) return;
			if (value.sqrMagnitude > 0f)
			{
				_previousInputDirection = _currentInputDirection;
			}
			_currentInputDirection = value;
			_facingDirection.x = ((_currentInputDirection.x == 0f) ? _facingDirection.x : _currentInputDirection.x);
			_facingDirection.y = ((_currentInputDirection.y == 0f) ? _facingDirection.y : _currentInputDirection.y);
		}

		public override void SetDirectionImmediate(Vector2 value)
		{
			if (IsUpgradeSelectionLocked) return;
			Debug.Log("SetDirectionImmediate");
			if (value.sqrMagnitude > 0f)
			{
				_previousInputDirection = _currentInputDirection;
			}
			_currentInputDirection = value;
			_facingDirection = _currentInputDirection;
		}

		public override void StopMovement()
		{
			_currentInputDirection = Vector2.zero;
			body.linearVelocity = Vector2.zero;
			body.angularVelocity = 0f;
		}

		public Vector2 GetLinearVelocity()
		{
			return body.linearVelocity;
		}

		public void ResetInputDirection()
		{
			_currentInputDirection = Vector2.zero;
		}

		public void Dash()
		{
			if (!CanCommitDash || _stateMachine == null) return;
			State current = _stateMachine.GetState();
			if (current == Moving || current == Hurt) _stateMachine.MakeTransition(Dashing);
		}

		private void OnFixedUpdateMoving()
		{
			body.linearVelocity = _currentInputDirection.normalized * PlayerStats.currentStats.moveSpeed;
			ApplyExternalForces();
		}

		private void ApplyExternalForces()
		{
			body.linearVelocity += DebuffForce + WindForce;
		}

		protected override void OnLateUpdateMoving()
		{
			playerAnimator.Movement(Mathf.Ceil(_currentInputDirection.magnitude), base.FacingDirection.x, base.FacingDirection.y);
		}

		private void OnEnterDashing()
		{
			_dashGeneration++;
			CurrentDashUseId = 0;
			_dashUseStarted = false;
			_isDashInitialized = false;
			_dashBuffer.Record();
		}

		private bool InitializeDash()
		{
			if (!CanCommitDash) return false;
			Vector2 direction = _currentInputDirection != Vector2.zero ? _currentInputDirection : _previousInputDirection;
			return TryGetDashMotionParameters(direction, transform.position, out DashMotionParameters motion) && SetDashParameters(motion);
		}

		/// <summary>Resolve this avatar's authored dash geometry without ownership, charge, or FSM mutations.</summary>
		public bool TryGetDashMotionParameters(Vector2 direction, Vector2 startPosition, out DashMotionParameters parameters)
		{
			parameters = default;
			if (!IsRuntimeInitialized || playerStats == null) return false;
			float dashDistance = PlayerStats.currentStats.dashDistance;
			Vector2 normalized = direction.normalized;
			Vector2 vector = startPosition;
			if (!IsFiniteDashValue(dashDistance) || dashDistance < 0f || !IsFiniteDashValue(vector.x) ||
				!IsFiniteDashValue(vector.y) || !IsFiniteDashValue(normalized.x) || !IsFiniteDashValue(normalized.y) ||
				normalized.sqrMagnitude <= 0f) return false;
			RaycastHit2D[] array = Physics2D.RaycastAll(vector, normalized, dashDistance, obstacleLayerMask);
			if (array.Length == 0)
			{
				return TryCreateDashMotion(vector, normalized, dashDistance, out parameters);
			}
			for (int i = 0; i < array.Length; i++)
			{
				if ((bool)array[i].collider && ((1 << array[i].collider.gameObject.layer) & (int)edgeLayerMask) != 0)
				{
					int num = Mathf.Max(i - 1, 0);
					return TryCreateDashMotion(vector, normalized, Vector2.Distance(vector, array[num].point), out parameters);
				}
			}
			float num2 = _obstacleCollider != null ? Mathf.Max(_obstacleCollider.transform.lossyScale.x, _obstacleCollider.transform.lossyScale.y) : 1f;
			float num3 = _obstacleCollider != null ? _obstacleCollider.radius * num2 : 0f;
			float num4 = dashDistance;
			for (int j = 0; j <= array.Length; j++)
			{
				Vector2 point = vector + normalized * num4;
				float num5 = float.MaxValue;
				RaycastHit2D[] array2 = array;
				for (int k = 0; k < array2.Length; k++)
				{
					RaycastHit2D raycastHit2D = array2[k];
					if ((bool)raycastHit2D.collider && raycastHit2D.collider.TryGetComponent<PolygonObstacleTrace>(out var _) && raycastHit2D.collider.OverlapPoint(point) && raycastHit2D.distance < num5)
					{
						num5 = raycastHit2D.distance;
					}
				}
				if (num5 >= float.MaxValue)
				{
					break;
				}
				num4 = Mathf.Max(num5 - num3 - dashObstacleMargin, 0f);
			}
			return TryCreateDashMotion(vector, normalized, num4, out parameters);
		}

		private bool TryCreateDashMotion(Vector2 start, Vector2 direction, float distance, out DashMotionParameters motion)
		{
			motion = default;
			float speed = PlayerStats.currentStats.dashSpeed;
			float duration = GetDashTotalTime(distance, speed, dashCurve);
			if (!IsFiniteDashValue(duration) || duration < 0f) return false;
			motion = new DashMotionParameters(start, direction, distance, duration, speed);
			return true;
		}

		private bool SetDashParameters(DashMotionParameters motion)
		{
			if (!CanCommitDash) return false;
			uint generation = _dashGeneration;
			ulong useId;
			try { useId = _tryCommitDashUse(motion); }
			catch { CancelDash(); throw; }
			SyncDashChargeMirror();
			if (useId == 0 || generation != _dashGeneration || !CanExecuteDash || _stateMachine.GetState() != Dashing) return false;
			CurrentDashUseId = useId;
			_currentDashDistance = motion.Distance;
			_totalDashTime = motion.Duration;
			_dashPeakSpeed = motion.PeakSpeed;
			_dashDirection = motion.Direction;
			_dashElapsedTime = 0f;
			_dashUseStarted = true;
			SetDashLayerMask();
			try
			{
				if (!dashAudio.IsNull) OptionalAudio.PlayOneShotAttached(dashAudio, base.gameObject);
				Action onStart = OnDashStart;
				if (onStart != null)
					foreach (Action callback in onStart.GetInvocationList())
					{
						if (generation != _dashGeneration || CurrentDashUseId != useId || !CanExecuteDash) break;
						callback();
					}
			}
			catch { CancelDash(); throw; }
			return generation == _dashGeneration && CurrentDashUseId == useId && CanExecuteDash && _stateMachine.GetState() == Dashing;
		}

		private float GetDashTotalTime(float distance, float peakSpeed, AnimationCurve curve, int samples = 100)
		{
			if (!IsFiniteDashValue(distance) || distance < 0f || !IsFiniteDashValue(peakSpeed) || peakSpeed <= 0f || curve == null || samples < 1)
				return float.NaN;
			if (distance == 0f) return 0f;
			float num = 0f;
			float num2 = 1f / (float)samples;
			for (int i = 0; i < samples; i++)
			{
				float time = (float)i * num2;
				float time2 = (float)(i + 1) * num2;
				float value = curve.Evaluate(time);
				float value2 = curve.Evaluate(time2);
				value2 = Mathf.Clamp01(value2);
				value = Mathf.Clamp01(value);
				num += (value + value2) * 0.5f * num2;
			}
			return num > 0f && IsFiniteDashValue(num) ? distance / (peakSpeed * num) : float.NaN;
		}

		private static bool IsFiniteDashValue(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

		private void SetDashLayerMask()
		{
			if (_hitboxCollider != null) _hitboxCollider.excludeLayers = dashExclusionLayerMask;
			if (_obstacleCollider != null) _obstacleCollider.excludeLayers = dashExclusionLayerMask;
		}

		private void SetDefaultLayerMask()
		{
			if (!IsRuntimeInitialized) return;
			if (_hitboxCollider != null) _hitboxCollider.excludeLayers = _defaultHitboxLayerMask;
			if (_obstacleCollider != null) _obstacleCollider.excludeLayers = _defaultObstacleLayerMask;
		}

		private void OnFixedUpdateDashing()
		{
			if (!_dashBuffer.IsValid)
			{
				if (!_isDashInitialized)
				{
					_dashBuffer.Consume();
					_isDashInitialized = true;
					if (!InitializeDash()) { CancelDash(); return; }
				}
				if (!CanExecuteDash || CurrentDashUseId == 0 || _stateMachine.GetState() != Dashing) return;
				if (_totalDashTime <= 0f) { _stateMachine.MakeTransition(Moving); return; }
				_dashElapsedTime += Time.fixedDeltaTime;
				float value = _dashElapsedTime / _totalDashTime;
				value = Mathf.Clamp01(value);
				float value2 = dashCurve.Evaluate(value);
				value2 = Mathf.Clamp01(value2);
				body.linearVelocity = _dashDirection * (_dashPeakSpeed * value2);
				if (value >= 1f)
				{
					_stateMachine.MakeTransition(Moving);
				}
			}
		}

		private void OnLateUpdateDashing()
		{
			if (playerAnimator != null) playerAnimator.Dash(base.FacingDirection.x, base.FacingDirection.y);
		}

		private void OnExitDashing()
		{
			_dashGeneration++;
			_dashBuffer.Consume();
			_isDashInitialized = false;
			if (body != null)
				body.linearVelocity = CanExecuteDash ? _currentInputDirection.normalized * PlayerStats.currentStats.moveSpeed : Vector2.zero;
			FinishDashUse();
		}

		private void OnDisable() => CancelDash();

		public void BruteforceKnockBack(Vector2 attackPosition, KnockbackSettings settings)
		{
			if (IsUpgradeSelectionLocked) return;
			Vector2 attackDirection;
			if (!(settings == null) && (settings.HasKnockback || settings.Staggers))
			{
				attackDirection = (Vector2)base.transform.position - attackPosition;
				attackDirection.Normalize();
				Knockback.onEnter = KnockBackOnEnter;
				_stateMachine?.MakeTransition(Knockback);
			}
			void KnockBackOnEnter()
			{
				playerAnimator.Hurt(base.FacingDirection.x, base.FacingDirection.y);
				_startPoint = base.transform.position;
				Vector2 vector = (settings.fixedDirection ? settings.direction.normalized : attackDirection);
				_endPoint = _startPoint + vector * settings.distance;
				_lastPosition = _startPoint;
				_elapsedTime = 0f;
				_knockBackTime = _maxKnockbackTime / settings.speedMultiplier;
				currentKnockbackSettings = settings;
			}
		}

		private void OnFixedUpdateKnockBack()
		{
			_elapsedTime += Time.deltaTime;
			if (_elapsedTime >= _knockBackTime)
			{
				_stateMachine.MakeTransition(Moving);
				return;
			}
			float time = _elapsedTime / _knockBackTime;
			float t = currentKnockbackSettings.speedCurve.Evaluate(time);
			Vector2 vector = Vector2.Lerp(_startPoint, _endPoint, t);
			Vector2 linearVelocity = (vector - _lastPosition) / Time.fixedDeltaTime;
			body.linearVelocity = linearVelocity;
			_lastPosition = vector;
		}

		public void SetAimDirection(Vector2 direction)
		{
			if (direction.magnitude < 0.2f)
			{
				if (_autoAim)
				{
					autoAim.Enable();
				}
				return;
			}
			if (_autoAim)
			{
				autoAim.Disable();
			}
			attackDirection = Vector2.ClampMagnitude(direction, 1f);
			attackDirection = direction.normalized;
		}

		public bool SetRuntimeAimDirection(Vector2 direction)
		{
			if (!float.IsFinite(direction.x) || !float.IsFinite(direction.y) ||
				direction.sqrMagnitude <= 0.000001f)
			{
				return false;
			}

			attackDirection = direction.normalized;
			return true;
		}

		public void SetAimPosition(Vector2 position)
		{
			if (!_autoAim && _inputCamera != null)
			{
				Vector2 vector = position;
				vector = _inputCamera.ScreenToWorldPoint(vector);
				attackDirection = vector - (Vector2)base.transform.position;
				attackDirection.Normalize();
			}
		}

		public void SetInputCamera(Camera camera) => _inputCamera = camera;

		private void OnAutoAimUpdate()
		{
			BaseEnemyController target = autoAim.GetTarget();
			if ((bool)target)
			{
				attackDirection = target.transform.position - base.transform.position;
			}
		}

		private void OnAutoAimOptionChanged(bool state)
		{
			_autoAim = state;
			if (state)
			{
				autoAim.Enable();
			}
			else
			{
				autoAim.Disable();
			}
		}

		public void Stun(float stunDuration)
		{
			if (!IsInvulnerable)
			{
				_stateMachine.MakeTransition(Stunned);
				_currentStunDuration = stunDuration;
			}
		}

		private void OnEnterStun()
		{
			StopMovement();
			playerAnimator.Hurt(base.FacingDirection.x, base.FacingDirection.y);
		}

		private void OnStunTick()
		{
			_currentStunDuration -= Time.deltaTime;
			if (_currentStunDuration <= 0f)
			{
				_stateMachine.MakeTransition(Moving);
			}
		}

		private void OnEnterHurt()
		{
			StopMovement();
			_hurtTime = hurtTime;
			playerAnimator.Hurt(base.FacingDirection.x, base.FacingDirection.y);
			OptionalAudio.PlayOneShot(hurtSound);
			ShowDamage();
			DecreaseHealth(_damageReceived);
			SetTimmedInvulnerability(invulnerabilityTime);
			if (!combatantBinding.IsAlive)
			{
				_stateMachine.MakeTransition(Dead);
			}
		}

		private void SetTimmedInvulnerability(float invulnerabilityTime)
		{
			SetInvulnerable(state: true);
			Action onInvulnerabilityEnd = null;
			Coroutine invulnerabilityRoutine = StartCoroutine(Wait.SetTimeout(invulnerabilityTime, delegate
			{
				SetInvulnerable(state: false);
				onInvulnerabilityEnd?.Invoke();
			}));
			_invulnerabilityCoroutines.Add(invulnerabilityRoutine);
			onInvulnerabilityEnd = delegate
			{
				_invulnerabilityCoroutines.Remove(invulnerabilityRoutine);
			};
		}

		private void OnUpdateHurt()
		{
			_hurtTime -= Time.deltaTime;
			if (_hurtTime < 0f)
			{
				_stateMachine.MakeTransition(Moving);
			}
		}

		public void Damage(int damage, Enum damageType)
		{
			if (!combatantBinding.AcceptsLocalMutations || IsInvulnerable)
			{
				return;
			}
			if (!(damageType is DamageType damageType2))
			{
				goto IL_007c;
			}
			if (damageType2 != DamageType.Normal)
			{
				if (damageType2 != DamageType.Thorns)
				{
					if (damageType2 != DamageType.Projectile)
					{
						goto IL_007c;
					}
					_damageReceived = (int)((float)damage * (1f - PlayerStats.StatMultipliers.attackStatsMultipliers.projectileDamageReceivedMultiplier));
				}
				else
				{
					_damageReceived = (int)((float)damage * (1f - PlayerStats.StatMultipliers.attackStatsMultipliers.contactDamageReceivedMultiplier));
				}
			}
			else
			{
				_damageReceived = damage;
			}
			goto IL_0083;
			IL_0083:
			_damageReceived = Mathf.Max(
				0,
				(int)((float)_damageReceived - PlayerStats.currentStats.dmgReduction));
			if (_damageReceived == 0)
			{
				return;
			}
			if (combatantBinding.CurrentHealth - _damageReceived > 0 || !TryUseMiracleOfBeatrice())
			{
				_stateMachine.MakeTransition(Hurt);
			}
			return;
			IL_007c:
			_damageReceived = damage;
			goto IL_0083;
		}

		public bool CheckIfMaxHealth()
		{
			return combatantBinding.CurrentHealth == combatantBinding.MaximumHealth;
		}

		public void DecreaseHealth(int value)
		{
			combatantBinding.ApplyDamage(value);
		}

		public void IncreaseHealth(int value)
		{
			combatantBinding.RestoreHealth(value);
		}

		private void ShowDamage()
		{
			PoolManager.Instance?.SpawnDamageNumber(
				GetEntityId(),
				base.transform,
				_damageReceived,
				DamageType.Normal,
				isCritical: false);
			if (spriteRenderer != null && base.gameObject.activeSelf && base.enabled)
			{
				if (_damageColorAnimation != null)
				{
					StopCoroutine(_damageColorAnimation);
				}
				_damageColorAnimation = StartCoroutine(DamageColorAnimation());
			}
		}

		private IEnumerator DamageColorAnimation()
		{
			if (_damagePropertyBlock == null)
			{
				_damagePropertyBlock = new MaterialPropertyBlock();
			}
			spriteRenderer.GetPropertyBlock(_damagePropertyBlock);
			_damagePropertyBlock.SetFloat(HitEffectBlendSID, 1f);
			_damagePropertyBlock.SetColor(HitEffectColorSID, Color.red);
			spriteRenderer.SetPropertyBlock(_damagePropertyBlock);
			yield return _damageColorAnimationWait;
			spriteRenderer.GetPropertyBlock(_damagePropertyBlock);
			_damagePropertyBlock.SetColor(HitEffectColorSID, Color.white);
			spriteRenderer.SetPropertyBlock(_damagePropertyBlock);
			yield return _damageColorAnimationWait;
			spriteRenderer.GetPropertyBlock(_damagePropertyBlock);
			_damagePropertyBlock.SetColor(HitEffectColorSID, Color.red);
			spriteRenderer.SetPropertyBlock(_damagePropertyBlock);
			yield return _damageColorAnimationWait;
			spriteRenderer.GetPropertyBlock(_damagePropertyBlock);
			_damagePropertyBlock.SetColor(HitEffectColorSID, Color.black);
			spriteRenderer.SetPropertyBlock(_damagePropertyBlock);
			yield return _damageColorAnimationWait;
			ResetDamageColor();
			_damageColorAnimation = null;
		}

		private void ResetDamageColor()
		{
			spriteRenderer.color = Color.white;
			spriteRenderer.GetPropertyBlock(_damagePropertyBlock);
			_damagePropertyBlock.SetFloat(HitEffectBlendSID, 0f);
			_damagePropertyBlock.SetColor(HitEffectColorSID, Color.white);
			spriteRenderer.SetPropertyBlock(_damagePropertyBlock);
		}

		private void OnEnterDead()
		{
			CancelDash();
			Died?.Invoke();
			if (!UsesNetworkLifecycle) GameEvents.Instance?.OnBeforePlayerDeath?.Invoke();
			OptionalAudio.PlayOneShot(deadSound);
			body.bodyType = RigidbodyType2D.Static;
			playerAnimator.Dead(base.FacingDirection.x, base.FacingDirection.y);
		}

		private void OnExitDead()
		{
			body.bodyType = RigidbodyType2D.Dynamic;
		}

		public void DeadAnimationFinished()
		{
			DeathPresentationCompleted?.Invoke();
			if (UsesNetworkLifecycle) return;
			base.gameObject.SetActive(value: false);
			GameEvents.Instance?.OnAfterPlayerDeath?.Invoke();
		}

		public void GiveUp()
		{
			CancelDash();
			_stateMachine.MakeTransition(GivingUp);
		}

		private void OnEnterGivingUp()
		{
			OptionalAudio.PlayOneShot(hurtSound);
			_stateMachine.MakeTransition(Dead);
		}

		private bool TryUseMiracleOfBeatrice()
		{
			if (PlayerStats.currentStats.reviveAmount <= 0)
			{
				return false;
			}
			PlayerStats.currentStats.reviveAmount--;
			int num = combatantBinding.MaximumHealth / 2;
			int num2 = num - combatantBinding.CurrentHealth;
			if (num2 > 0)
			{
				combatantBinding.RestoreHealth(num2);
			}
			SetTimmedInvulnerability(1f);
			return true;
		}

		public void GainUltimateCharge()
		{
			if (UsesNetworkLifecycle)
				throw new InvalidOperationException("Network Ultimate charge must be granted by the server reward entry.");
			_ultimateCharge = true;
			GameEvents.Instance.UltimateGained?.Invoke();
		}

		public void ResetUltimateCharge()
		{
			if (UsesNetworkLifecycle)
				throw new InvalidOperationException("Network Ultimate charge belongs to the player runtime checkpoint.");
			_ultimateCharge = false;
		}

		public void UltimateAction()
		{
			if (IsUpgradeSelectionLocked) return;
			if (_tryUseNativeUltimate != null)
			{
				if (isActiveAndEnabled && IsRuntimeInitialized && (!UsesNetworkLifecycle || IsLocalOwnerBound) &&
					combatantBinding.Combatant.IsAlive) _tryUseNativeUltimate();
				return;
			}
			if (UsesNetworkLifecycle) return;
			if (_ultimateCharge)
			{
				_ultimateCharge = false;
				ultimateAttackManager.gameObject.SetActive(value: true);
				GameEvents.Instance.UltimateUsed?.Invoke();
				ControllerManager.Instance.OverrideGameController<UltimateAttackController>();
			}
		}

		public float GetLootPullArea()
		{
			return PlayerStats.currentStats.pullArea;
		}

		public Vector2 GetLootCollectorPosition()
		{
			return base.transform.position;
		}

		public void Interact()
		{
			if (IsUpgradeSelectionLocked) return;
			interactionFinder.TryInteract();
		}

		public void DisableInteractor()
		{
			if (interactor != null) interactor.enabled = false;
		}

		public void EnableInteractor()
		{
			if (interactor != null) interactor.enabled = true;
		}

		public override void OnPausePausables()
		{
			base.OnPausePausables();
			DisableInteractor();
		}

		public override void OnResumePausables()
		{
			base.OnResumePausables();
			EnableInteractor();
		}

		public void IncreaseXP(float xp)
		{
			if (UsesNetworkLifecycle)
				throw new InvalidOperationException("Network XP is awarded by NetworkModifierSelection; the legacy Leveler is disabled.");
			float num = (GameEvents.Instance.IsMagnetOn ? 0.5f : 1f);
			leveler.IncreaseXP(xp * PlayerStats.currentStats.xpModifier * num);
		}

		public void SetInvulnerable(bool state)
		{
			_invulnerabilityCount = Mathf.Clamp(state ? (_invulnerabilityCount + 1) : (_invulnerabilityCount - 1), 0, int.MaxValue);
			Debug.Log("PLAYER INVULNERABILITY try set : " + state + " Invulnerability count now at " + _invulnerabilityCount + " invulnerability state now at " + _isInvulnerable);
		}

		public void TeleportAnimation()
		{
			playerAnimator.ResetAnimancer();
			playerAnimator.Idle(base.FacingDirection.x, base.FacingDirection.y);
			playerAnimator.Teleport();
			playerVFX.TriggerTeleportVFX();
			if (!teleportSound.IsNull)
			{
				OptionalAudio.PlayOneShotAttached(teleportSound, base.gameObject);
			}
		}

		public void DebugInvulnerabilitySwitch()
		{
			DebugIsInvulnerable = !DebugIsInvulnerable;
			Debug.Log($"DevDebug Invunerability: {DebugIsInvulnerable}");
		}
	}
}
