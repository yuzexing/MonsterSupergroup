using System;
using System.Collections.Generic;
using AstralShift.FSM;
using AstralShift.HellMaiden.Common;
using AstralShift.HellMaiden.GameStats;
using AstralShift.HellMaiden.Items;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.Helpers;
using AstralShift.Helpers.Attributes;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyController : BaseEnemyController
	{
		[SerializeField]
		protected Transform _target;

		[Header("References")]
		public Rigidbody2D rigidBody;

		public EnemyAnimator enemyAnimator;

		public Transform damagePosition;

		protected GenericPooler<EnemyController> _enemyPool;

		protected StateMachine _stateMachine;

		public State Moving;

		public State Warning;

		public State Attacking;

		public State Recovery;

		public State Knockback;

		public State Dead;

		public State InstantDead;

		public State Deactivating;

		public State Deactivated;

		[Header("Loot Drop Settings")]
		public bool overrideGlobalLootSettings;

		public LootSettingsData lootSettings;

		public BaseEnemyMovement _currentMovementScript;

		public Vector2 previousFacingDirection;

		[Header("Attack Settings")]
		public EnemyAttack attackScript;

		public float attackCooldown = 0.5f;

		public Vector2 attackPivot = Vector2.zero;

		public float attackDistance = 0.5f;

		public bool attackOnCameraBounds;

		public float cameraBoundsDistanceMultiplier = 0.8f;

		public bool hasAttackAnimation = true;

		public bool facingPlayerDuringWarning;

		public bool facingPlayerDuringAttack;

		[SerializeField]
		private bool _cancelAttackOnKnockback = true;

		public bool alwaysAttacking;

		public float lastAttackTime = float.NegativeInfinity;

		public bool attackOnDeath;

		public bool stopForAttack = true;

		public EnemyDefaultMovement defaultMovement;

		public bool usesPathfinding = true;

		public EnemyAILerpMovement aILerpMovement;

		public LayerMask obstaclesLayerMask;

		public float obstacleDetectionCastRadius = 1.3f;

		public Vector2 obstacleDetectionCastOffset;

		public float obstacleDetectionCastDistance = 2f;

		public float spawnReferenceRadius = 0.5f;

		protected float _defaultRigidbodyMass;

		protected Vector3 _destination;

		[ReadOnly]
		public bool allowRubberband = true;

		public bool rubberbandStatsReset = true;

		[Tooltip("If this value is higher than current time, kill enemy on rubberband")]
		[ReadOnly]
		public float endTime;

		public Direction direction;

		public float angle;

		protected const float _stuckMovementThreshold = 0.2f;

		private Vector2 _lastPosition;

		private float _accumulatedMovement;

		private float _stuckTimer;

		protected bool _isStuck;

		protected bool _canBeStuck = true;

		protected bool _isAvoidingObstacle;

		protected RaycastHit2D[] _stuckHitResults = new RaycastHit2D[1];

		protected const float StuckTimeThreshold = 0.33f;

		private const float StuckMassIncrement = 30f;

		public Collider2D collider;

		public EnemyHurtbox hurtBox;

		private bool _dropLoot = true;

		private bool _cullingOptimizationsEnabled = true;

		public Transform Target
		{
			get
			{
				return _target;
			}
			set
			{
				_target = value;
			}
		}

		protected Vector2 GetTargetPosition => (!Target) ? base.Transform.position : Target.position;

		public GenericPooler<EnemyController> EnemyPool
		{
			get
			{
				return _enemyPool;
			}
			set
			{
				_enemyPool = value;
			}
		}

		public StateMachine StateMachine => _stateMachine;

		public bool WasAttacking
		{
			get
			{
				if ((bool)attackScript)
				{
					if (StateMachine.PreviousState != Attacking)
					{
						return StateMachine.PreviousState == Warning;
					}
					return true;
				}
				return false;
			}
		}

		public override bool IsDead
		{
			get
			{
				if (!IsInDeadState)
				{
					return stats.Health <= 0;
				}
				return true;
			}
		}

		public bool IsInDeadState
		{
			get
			{
				if (_stateMachine.GetState() != Dead)
				{
					return _stateMachine.GetState() == InstantDead;
				}
				return true;
			}
		}

		public bool IsInKnockbackState => _stateMachine.GetState() == Knockback;

		[Header("Movement Settings")]
		public BaseEnemyMovement Movement => _currentMovementScript;

		public Vector3 Destination => _destination;

		public Vector2 FacingDirection => Movement.Direction;

		private float StuckMass => _defaultRigidbodyMass + 30f;

		public bool SpawnedLoot { get; private set; }

		public event Action OnKill;

		public event Action OnDispose;

		public override void Init(int id)
		{
			base.ID = id;
			try
			{
				status.Init(this);
			}
			catch (Exception)
			{
			}
			InitValues();
			InitializeStateMachine();
			enemyAnimator.Init(this);
			hurtBox.ActivateCollider(state: true);
			direction = Direction.None;
			angle = 0f;
			OnInit?.Invoke();
			OnInit = null;
			OnInitPersist?.Invoke();
			base.gameObject.SetActive(value: true);
		}

		public void ResetEnemyCondition()
		{
			stats.Reset();
			status.ClearAllStatus();
		}

		protected virtual void InitializeStateMachine()
		{
			if (_stateMachine != null)
			{
				_stateMachine.Reset();
				return;
			}
			_stateMachine = new StateMachine("EnemyAI");
			Moving = new State("Moving");
			Warning = new State("Warning");
			Attacking = new State("Attacking");
			Recovery = new State("Recovery");
			Knockback = new State("Knockback");
			Dead = new State("Dead");
			Deactivating = new State("Deactivating");
			Deactivated = new State("Deactivated");
			InstantDead = new State("InstantDead");
			_stateMachine.AddTransition(Moving, Attacking);
			_stateMachine.AddTransition(Moving, Warning);
			_stateMachine.AddTransition(Warning, Knockback);
			_stateMachine.AddTransition(Warning, Attacking);
			_stateMachine.AddTransition(Attacking, Recovery);
			_stateMachine.AddTransition(Recovery, Warning);
			_stateMachine.AddTransition(Attacking, Knockback);
			_stateMachine.AddTransition(Recovery, Moving);
			_stateMachine.AddTransition(Recovery, Knockback);
			_stateMachine.AddTransition(Moving, Knockback);
			_stateMachine.AddTransition(Knockback, Moving);
			_stateMachine.AddTransition(Knockback, Warning);
			_stateMachine.AddTransition(Knockback, Attacking);
			_stateMachine.AddAnyTransition(Dead);
			_stateMachine.AddAnyTransition(InstantDead);
			_stateMachine.AddAnyTransition(Deactivating);
			_stateMachine.AddTransition(Deactivating, Deactivated);
			_stateMachine.AddTransition(Deactivated, Moving);
			Moving.onEnter = delegate
			{
				Movement.FreezeRigidbody(state: false);
				RefreshMovementMethod();
				_canRubberband = true;
			};
			Moving.onUpdateTick = TryAttack;
			Moving.onExit = delegate
			{
				if (stopForAttack)
				{
					SetDefaultMovement();
					EnemyAIManager.Instance.UnRegisterStuckHordeEnemy(this);
					DisableMovement();
				}
				_canRubberband = false;
			};
			attackScript.controller = this;
			if (hasAttackAnimation)
			{
				attackScript.enemyAnimator = enemyAnimator;
				attackScript.onAttackWarningEnd = OnAttackWarningEnd;
				attackScript.onAttackEnd = OnAttackEnd;
				attackScript.onRecoveryEnd = OnRecoveryEnd;
				Warning.onEnter = delegate
				{
					attackScript.AttackWarningEnter();
					Movement.SetFacingDirection(GetTargetPosition - (Vector2)base.transform.position);
					enemyAnimator.AttackWarning(FacingDirection.x, FacingDirection.y);
					previousFacingDirection = FacingDirection;
				};
				Warning.onLateUpdateTick = delegate
				{
					attackScript.AttackWarningTick();
					if (facingPlayerDuringWarning)
					{
						Movement.SetFacingDirection(GetTargetPosition - (Vector2)base.transform.position);
						enemyAnimator.AttackWarning(FacingDirection.x, FacingDirection.y);
					}
				};
				Warning.onExit = attackScript.AttackWarningExit;
				Attacking.onEnter = delegate
				{
					attackScript.AttackEnter();
					enemyAnimator.Attack(previousFacingDirection.x, previousFacingDirection.y);
				};
				Attacking.onLateUpdateTick = delegate
				{
					attackScript.AttackTick();
					if (facingPlayerDuringAttack)
					{
						Movement.SetFacingDirection(GetTargetPosition - (Vector2)base.transform.position);
						previousFacingDirection = FacingDirection;
						enemyAnimator.Attack(FacingDirection.x, FacingDirection.y);
					}
				};
				Attacking.onExit = attackScript.AttackExit;
				Recovery.onEnter = delegate
				{
					enemyAnimator.Recovery(previousFacingDirection.x, previousFacingDirection.y);
					Movement.SetFacingDirection(previousFacingDirection);
					attackScript.RecoveryEnter();
				};
				State recovery = Recovery;
				recovery.onLateUpdateTick = (Action)Delegate.Combine(recovery.onLateUpdateTick, new Action(attackScript.RecoveryTick));
				Recovery.onExit = delegate
				{
					Movement.FreezeRigidbody(state: false);
					attackScript.RecoveryExit();
				};
			}
			Knockback.onExit = delegate
			{
				Movement.FreezeRigidbody(state: false);
				EnableMovement();
			};
			Moving.onLateUpdateTick = delegate
			{
				if (Target != null)
				{
					Movement.SetFacingDirection(GetTargetPosition - (Vector2)base.transform.position);
					enemyAnimator.Movement(FacingDirection.x, FacingDirection.y);
				}
			};
			Dead.onEnter = delegate
			{
				ActivateColliders(activate: false);
				if (((bool)attackScript && (StateMachine.PreviousState == Attacking || StateMachine.PreviousState == Warning)) || alwaysAttacking)
				{
					attackScript.CancelAttack();
				}
				Movement.FreezeRigidbody(state: true);
				hurtBox.ActivateCollider(state: false);
				EnemyAIManager.Instance.UnRegisterEnemy(this);
				enemyAnimator.DeathAnimation(FacingDirection, SpawnLootAndDispose);
			};
			Dead.onExit = delegate
			{
				Movement.FreezeRigidbody(state: false);
			};
			InstantDead.onEnter = delegate
			{
				ActivateColliders(activate: false);
				if (((bool)attackScript && (StateMachine.PreviousState == Attacking || StateMachine.PreviousState == Warning)) || alwaysAttacking)
				{
					attackScript.CancelAttack();
				}
				if (_dropLoot)
				{
					SpawnLoot();
				}
				EnemyAIManager.Instance.UnRegisterEnemy(this);
				Dispose();
			};
			Deactivating.onEnter = delegate
			{
				if (StateMachine.PreviousState == Dead)
				{
					Debug.LogWarning("Enemy shouldn't be in Dead state here!");
				}
				if (StateMachine.PreviousState == Knockback)
				{
					Debug.Log("Enemy deactivated in Knockback state!");
				}
				if (((bool)attackScript && (StateMachine.PreviousState == Attacking || StateMachine.PreviousState == Warning)) || alwaysAttacking)
				{
					attackScript.CancelAttack();
				}
				EnemyAIManager.Instance.UnRegisterStuckHordeEnemy(this);
				_isStuck = false;
				_isAvoidingObstacle = false;
				_accumulatedMovement = 0f;
				_stuckTimer = 0f;
				enemyAnimator.PlayDeSpawnAnimation(delegate
				{
					_stateMachine.MakeTransition(Deactivated);
				});
			};
			Deactivated.onEnter = delegate
			{
				base.gameObject.SetActive(value: false);
				enemyAnimator.FinalizeDeSpawnAnimation();
			};
			Deactivated.onExit = delegate
			{
				base.gameObject.SetActive(value: true);
			};
			_stateMachine.SetInitialState(Moving);
			void SpawnLootAndDispose()
			{
				if (_dropLoot)
				{
					SpawnLoot();
				}
				_dropLoot = true;
				this.OnKill?.Invoke();
				this.OnKill = null;
				Dispose();
			}
		}

		protected virtual void InitValues()
		{
			stats.Reset();
			_transform = base.transform;
			_defaultRigidbodyMass = rigidBody.mass;
			if (usesPathfinding)
			{
				aILerpMovement.Init(this);
			}
			defaultMovement.Init(this);
			ResetMovementMethod();
			Movement.FreezeRigidbody(state: false);
			ActivateColliders(activate: true);
			allowRubberband = true;
			EnemyAIManager.Instance.RegisterEnemy(this);
			hurtBox.OnDamageWeapon += Damage;
			hurtBox.OnDamageGeneric += Damage;
			enemyAnimator.ResetHurtBlinkColor();
			SpawnedLoot = false;
			attackScript.Target = Target;
			this.OnKill = null;
			this.OnDispose = null;
		}

		public void Deactivate()
		{
			_stateMachine.MakeTransition(Deactivating);
		}

		public void Activate()
		{
			_stateMachine.MakeTransition(Moving);
		}

		public override void Dispose()
		{
			if ((bool)spriteRenderer)
			{
				spriteRenderer.transform.rotation = Quaternion.identity;
			}
			rigidBody.mass = _defaultRigidbodyMass;
			_knockbackSettingsOverride = null;
			hurtBox.OnDamageWeapon -= Damage;
			hurtBox.OnDamageGeneric -= Damage;
			this.OnDispose?.Invoke();
			this.OnDispose = null;
			EnemyPool?.Return(this);
		}

		public virtual void RunUpdate()
		{
			_stateMachine?.UpdateTick();
		}

		public void RunFixedUpdate()
		{
			_currentMovementScript?.MovementUpdate();
		}

		public void RunLateUpdate()
		{
			_stateMachine?.LateUpdateTick();
		}

		public void TransitionToMoving()
		{
			_stateMachine?.MakeTransition(Moving);
		}

		public void TransitionToWarning()
		{
			_stateMachine?.MakeTransition(Warning);
		}

		public void TransitionToAttacking()
		{
			_stateMachine?.MakeTransition(Attacking);
		}

		public void TransitionToRecovery()
		{
			_stateMachine?.MakeTransition(Recovery);
		}

		protected void TransitionToKnockBack()
		{
			_stateMachine?.MakeTransition(Knockback);
		}

		protected void TransitionToDead(bool isInstant = false)
		{
			_stateMachine?.MakeTransition(isInstant ? InstantDead : Dead);
		}

		protected void TransitionToPreviousState()
		{
			_stateMachine?.MakeTransition(_stateMachine.PreviousState);
		}

		public virtual void UpdateDestination()
		{
			_destination = GetTargetPosition;
			_distanceToTarget = (_destination - (Vector3)base.MovementCenterPosition).magnitude;
			if (_currentMovementScript != null)
			{
				_currentMovementScript.Destination = _destination;
			}
		}

		public virtual void RefreshMovementMethod()
		{
			_canBeStuck = true;
			if (_isAvoidingObstacle)
			{
				SetPathfindingMovement();
			}
			else
			{
				SetDefaultMovement();
			}
			EnableMovement();
		}

		protected virtual void SetDefaultMovement()
		{
			_currentMovementScript = defaultMovement;
			rigidBody.mass = _defaultRigidbodyMass;
		}

		protected virtual void SetPathfindingMovement()
		{
			_currentMovementScript = aILerpMovement;
			rigidBody.mass = StuckMass;
		}

		protected virtual void EnableMovement()
		{
			Movement.ResumeMovement();
		}

		protected virtual void DisableMovement()
		{
			_canBeStuck = false;
			if (usesPathfinding)
			{
				aILerpMovement.StopMovement();
			}
			defaultMovement.StopMovement();
		}

		protected virtual void ResetMovementMethod()
		{
			if (usesPathfinding)
			{
				aILerpMovement.SetTransform(_transform);
				aILerpMovement.SetRigidBody(rigidBody);
				aILerpMovement.switchPathInterpolationSpeed = stats.Speed;
			}
			defaultMovement.SetTransform(_transform);
			defaultMovement.SetRigidBody(rigidBody);
			defaultMovement.enemyController = this;
			SetDefaultMovement();
			_isAvoidingObstacle = false;
			_isStuck = false;
			_lastPosition = base.MovementCenterPosition;
			_accumulatedMovement = 0f;
			_stuckTimer = 0f;
		}

		public virtual void CheckIfStuck()
		{
			if (!_canBeStuck || _isStuck)
			{
				return;
			}
			_stuckTimer += Time.fixedDeltaTime;
			if (_stuckTimer >= 0.33f)
			{
				Vector2 vector = base.transform.position;
				if ((vector - _lastPosition).sqrMagnitude < 0.040000003f)
				{
					_isStuck = true;
					EnemyAIManager.Instance.RegisterStuckHordeEnemy(this);
				}
				_lastPosition = vector;
				_stuckTimer = 0f;
			}
		}

		public virtual bool UnStuckCheck()
		{
			Vector2 vector = base.MovementCenterPosition + obstacleDetectionCastOffset;
			Vector2 vector2 = (Vector2)_destination - vector;
			ContactFilter2D contactFilter = new ContactFilter2D
			{
				useLayerMask = true,
				layerMask = obstaclesLayerMask,
				maxDepth = float.PositiveInfinity
			};
			int num = Physics2D.CircleCast(vector, obstacleDetectionCastRadius, vector2.normalized, contactFilter, _stuckHitResults, obstacleDetectionCastDistance);
			_isAvoidingObstacle = num != 0;
			_isStuck = _isAvoidingObstacle;
			return _isAvoidingObstacle;
		}

		public void ToggleCullingOptimizations(bool enabled)
		{
			_cullingOptimizationsEnabled = enabled;
		}

		public void SetCullingOptimizations(bool state)
		{
			if (_cullingOptimizationsEnabled)
			{
				if (state)
				{
					defaultMovement.SetOptimizations(state: true);
				}
				else
				{
					defaultMovement.SetOptimizations(state: false);
				}
			}
		}

		public override void ApplyKnockBack(Vector2 attackPosition, WeaponBehaviour weaponBehaviour, bool isFatal)
		{
			if (IsInKnockbackState)
			{
				return;
			}
			KnockbackSettings settings;
			if ((bool)_knockbackSettingsOverride)
			{
				settings = _knockbackSettingsOverride;
				_knockbackSettingsOverride = null;
			}
			else
			{
				settings = weaponBehaviour.KnockbackSettings;
			}
			if (!settings || (!settings.HasKnockback && !settings.Staggers) || stats.KnockBackMultiplier <= 0f)
			{
				if (isFatal)
				{
					Kill();
				}
				return;
			}
			enemyAnimator.Hurt(FacingDirection.x, FacingDirection.y);
			Vector2 vector = (hurtBox ? hurtBox.GetPosition() : ((Vector2)base.Transform.position));
			Vector2 attackDirection = vector - attackPosition;
			attackDirection.Normalize();
			Knockback.onEnter = KnockBackOnEnter;
			TransitionToKnockBack();
			void KnockBackOnEnter()
			{
				if (WasAttacking && _cancelAttackOnKnockback)
				{
					attackScript.CancelAttack();
				}
				if (isFatal)
				{
					ActivateColliders(activate: false);
				}
				Movement.FreezeRigidbody(state: false);
				DisableMovement();
				Movement.KnockBack(attackDirection, settings, OnCompleteAction, weaponBehaviour.KnockBackMultiplierSum);
			}
			void OnCompleteAction()
			{
				if (stats.Health <= 0)
				{
					Kill();
				}
				else if (WasAttacking && !_cancelAttackOnKnockback)
				{
					TransitionToPreviousState();
				}
				else
				{
					TransitionToMoving();
				}
			}
		}

		public override void BruteforceKnockBack(Vector2 attackPosition, KnockbackSettings settings)
		{
			Vector2 attackDirection;
			if (!IsDead && !base.IsImmune && !IsInKnockbackState && !(settings == null) && (settings.HasKnockback || settings.Staggers) && !attackScript.OverrideKnockback)
			{
				Vector2 vector = (hurtBox ? hurtBox.GetPosition() : ((Vector2)base.Transform.position));
				attackDirection = vector - attackPosition;
				attackDirection.Normalize();
				Knockback.onEnter = KnockBackOnEnter;
				TransitionToKnockBack();
			}
			void KnockBackOnEnter()
			{
				if (WasAttacking && _cancelAttackOnKnockback)
				{
					attackScript.CancelAttack();
				}
				if (stats.Health <= 0)
				{
					ActivateColliders(activate: false);
				}
				Movement.FreezeRigidbody(state: false);
				DisableMovement();
				Movement.KnockBack(attackDirection, settings, OnCompleteAction, 1f);
			}
			void OnCompleteAction()
			{
				if (stats.Health <= 0)
				{
					Kill();
				}
				else if (WasAttacking && !_cancelAttackOnKnockback)
				{
					TransitionToPreviousState();
				}
				else
				{
					TransitionToMoving();
				}
			}
		}

		protected virtual bool IsInAttackDistance()
		{
			if (attackOnCameraBounds)
			{
				if (hasAttackAnimation && ProCamera2DHelpers.IsWithinCameraBounds(Bounds, cameraBoundsDistanceMultiplier))
				{
					return Time.time - lastAttackTime > attackCooldown;
				}
				return false;
			}
			if ((bool)Target && hasAttackAnimation && _distanceToTarget <= attackDistance)
			{
				return Time.time - lastAttackTime > attackCooldown;
			}
			return false;
		}

		protected virtual void TryAttack()
		{
			if (IsInAttackDistance())
			{
				Attack();
			}
		}

		public void Attack()
		{
			TransitionToWarning();
		}

		protected virtual void OnAttackWarningEnd()
		{
			TransitionToAttacking();
		}

		protected virtual void OnAttackEnd()
		{
			TransitionToRecovery();
		}

		protected virtual void OnRecoveryEnd()
		{
			lastAttackTime = Time.time;
			TransitionToMoving();
		}

		public void ActivateColliders(bool activate)
		{
			collider.enabled = activate;
			hurtBox.ActivateCollider(activate);
		}

		public override void Damage(Vector2 attackPosition, WeaponBehaviour weapon, DamageType damageType)
		{
			if (IsDead || base.IsImmune)
			{
				return;
			}
			DamageInfo damageInfo = weapon.CalculateDamage(this);
			ApplyDamage(damageInfo);
			ApplyOnHitEffects(weapon, damageInfo);
			ShowDamageNumbers(damageInfo, damageType, damagePosition);
			bool flag = stats.Health <= 0;
			if (flag)
			{
				if (attackOnDeath)
				{
					Attack();
					return;
				}
				RunStatsTracker.Instance?.RegisterWeaponKill(weapon);
				RunStatsTracker.Instance.PlayerStatsEntry?.RegisterDefeatedEnemy(selectedName);
				ApplyOnKillEffects(weapon);
			}
			enemyAnimator.HurtBlinkAnimation();
			ApplyKnockBack(attackPosition, weapon, flag);
		}

		public override void Damage(int value, DamageType damageType)
		{
			if (!IsDead && !base.IsImmune)
			{
				ApplyDamage(value);
				ShowDamageNumbers(GetEntityId(), value, damageType, isCritical: false, damagePosition);
				bool num = stats.Health <= 0;
				enemyAnimator.HurtBlinkAnimation();
				if (num)
				{
					RunStatsTracker.Instance.PlayerStatsEntry?.RegisterDefeatedEnemy(selectedName);
					Kill();
				}
			}
		}

		public virtual void Kill(bool instant, bool dropXp)
		{
			_dropLoot = dropXp;
			if (instant)
			{
				TransitionToDead(isInstant: true);
			}
			else
			{
				Kill();
			}
		}

		public virtual void Kill()
		{
			TransitionToDead();
		}

		private void SpawnLoot()
		{
			if (overrideGlobalLootSettings)
			{
				List<WorldItem> overridenLoot = LootManager.Instance.GetOverridenLoot(stats.XP, lootSettings);
				if (overridenLoot != null && overridenLoot.Count > 0)
				{
					for (int i = 0; i < overridenLoot.Count; i++)
					{
						Vector2 normalized = UnityEngine.Random.insideUnitCircle.normalized;
						overridenLoot[i].Show();
						overridenLoot[i].transform.position = base.transform.position + (Vector3)normalized * UnityEngine.Random.Range(0.75f, 1f);
						SpawnedLoot = true;
					}
				}
				if (lootSettings.dropChest)
				{
					LootManager.Instance.GetChest().transform.position = base.transform.position;
				}
			}
			else
			{
				WorldItem globalLoot = LootManager.Instance.GetGlobalLoot(stats.XP);
				if ((bool)globalLoot)
				{
					globalLoot.Show();
					globalLoot.transform.position = base.transform.position;
					SpawnedLoot = true;
				}
			}
		}

		public override Vector2 GetHurtBoxPosition()
		{
			return hurtBox.GetBounds().center;
		}
	}
}
