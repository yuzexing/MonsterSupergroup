using System;
using AstralShift.Control.Controllers;
using AstralShift.FSM;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.AI.Enemy.Boss;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Data;
// using AstralShift.HellMaiden.Dialogue;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.HellMaiden.Scenes;
using AstralShift.HellMaiden.UI;
using AstralShift.Helpers.Attributes;
using AstralShift.Helpers.DialogueHelpers;
using AstralShift.Managers;
using AstralShift.QTI.Helpers.Attributes;
// using PixelCrushers.DialogueSystem;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Serialization;

namespace AstralShift.HellMaiden.AI.Boss
{
	public class BossController : BaseEnemyController
	{
		[Header("General References")]
		[SerializeField]
		protected Rigidbody2D body;

		[SerializeField]
		protected BossAnimator animator;

		public BaseEnemyMovement movementController;

		[SerializeField]
		protected BossAttackController attackController;

		[SerializeField]
		protected EnemyHurtbox hurtBox;

		// [SerializeField]
		// protected AstralDialogueActor actor;

		// [VariablePopup(false)]
		// public string killPlayerTrigger;

		[Header("Damage Settings")]
		[SerializeField]
		protected Transform damageNumbersPivot;

		[Header("Phase Settings")]
		[SerializeField]
		protected BossPhase[] phases;

		[SerializeField]
		[ReadOnly]
		protected int currentPhaseIndex;

		[SerializeField]
		private Animator sphereAnimator;

		private Transform playerTransform;

		protected StateMachine _phasesStateMachine;

		protected State _intro;

		protected State _phase;

		protected State _intermission;

		protected State _outro;

		public Action OnToPhaseTransition;

		public Action OnIntermissionTransition;

		// [Header("Damage Settings")]
		// [Tooltip("The conversation to start.")]
		// [ConversationPopup(true, false)]
		// [SerializeField]
		// private string conversation;

		[SerializeField]
		protected int hurt_entryId;

		[SerializeField]
		protected int death_entryId;

		[SerializeField]
		protected bool barksOn;

		[SerializeField]
		private bool isMidBoss;

		// [VariablePopup(false)]
		// public string hasKilledBossTrigger;
		//
		// [VariablePopup(false)]
		// public string killBossCountTrigger;

		[SerializeField]
		[ConditionalHide("isMidBoss", true)]
		private GameObject mapEdges;

		[SerializeField]
		[Tooltip("True if this boss should activate another boss on init.")]
		private BossController twinBoss;

		[FormerlySerializedAs("minosScrolling")]
		[SerializeField]
		protected Animator backgroundAnimator;

		[SerializeField]
		private AnimationClip backgroundIn;

		[SerializeField]
		private AnimationClip backgroundOut;

		[SerializeField]
		private float freakoutAttacksDuration;

		public Transform finalDestination;

		public Shooter[] shooters;

		[FormerlySerializedAs("playableDirector")]
		public PlayableDirector freakoutTimeline;

		public UnityEvent OnKillMidBoss;

		public Rigidbody2D RigidBody => body;

		public BossAnimator Animator => animator;

		public BaseEnemyMovement Movement => movementController;

		// public AstralDialogueActor Actor => actor;

		public int CurrentPhaseIndex => currentPhaseIndex;

		public bool IsInIntro => _phasesStateMachine.GetState() == _intro;

		public bool IsInIntermission => _phasesStateMachine.GetState() == _intermission;

		public bool BarksOn
		{
			get
			{
				return barksOn;
			}
			private set
			{
				barksOn = value;
			}
		}

		public override bool IsDead => _phasesStateMachine.GetState() == _outro;

		private void Start()
		{
			Init(-1);
		}

		public override void Init(int id)
		{
			base.ID = id;
			SetImmunity(state: true);
			if ((bool)backgroundAnimator)
			{
				backgroundAnimator.gameObject.SetActive(value: true);
			}
			playerTransform = GameDirector.Instance.Player.transform;
			movementController.SetTransform(base.transform);
			EnemyAIManager.Instance.RegisterEnemy(this);
			attackController.Init(this);
			hurtBox.OnDamageWeapon += Damage;
			hurtBox.OnDamageGeneric += Damage;
			hurtBox.ActivateCollider(state: true);
			stats.Reset();
			if ((bool)twinBoss)
			{
				twinBoss.stats = stats;
				twinBoss.gameObject.SetActive(value: true);
			}
			status.Init(this);
			_transform = base.transform;
			InitPhaseLogicStateMachine();
			GameEvents instance = GameEvents.Instance;
			instance.OnBeforePlayerDeath = (Action)Delegate.Combine(instance.OnBeforePlayerDeath, new Action(PauseStateMachineOnPlayerDeath));
			GameEvents instance2 = GameEvents.Instance;
			instance2.OnAfterPlayerDeath = (Action)Delegate.Combine(instance2.OnAfterPlayerDeath, new Action(PlayerKilled));
			OnInit?.Invoke();
			OnInit = null;
			OnInitPersist?.Invoke();
		}

		private void InitPhaseLogicStateMachine()
		{
			_phasesStateMachine = new StateMachine("Boss Phases StateMachine");
			_intro = new State("Boss Intro");
			_phase = new State("Boss Phase");
			_intermission = new State("Boss Phase Intermission");
			_outro = new State("Boss Phase Outro");
			_intro.onEnter = delegate
			{
				SetPhaseIndex(0);
			};
			_intro.onUpdateTick = TransitionToAttacking;
			_intro.onExit = delegate
			{
			};
			_phase.onEnter = delegate
			{
				SetImmunity(state: false);
				EvaluatePhaseProgression();
				TransitionToAttacking();
			};
			_phase.onUpdateTick = delegate
			{
				EvaluatePhaseProgression();
			};
			_phase.onExit = delegate
			{
			};
			float intermissionTimestamp = 0f;
			_intermission.onEnter = delegate
			{
				animator.ResetAnimationCallbacks();
				intermissionTimestamp = Time.time;
				switch (currentPhaseIndex)
				{
				case 0:
					animator.Hurt(0f, 0f);
					// BarkLine(conversation, hurt_entryId);
					attackController.TransitionToIntermission();
					break;
				case 1:
					animator.Hurt(0f, 0f);
					// BarkLine(conversation, death_entryId);
					if (!isMidBoss)
					{
						TransitionToFreakout();
					}
					else
					{
						attackController.TransitionToIntermission();
					}
					break;
				case 2:
					Debug.Log("Theres no way this runs btw!");
					animator.Hurt(0f, 0f);
					// BarkLine(conversation, hurt_entryId);
					break;
				}
			};
			_outro.onEnter = delegate
			{
				if (!isMidBoss)
				{
					attackController.DisposeAllAttacks();
					if (!twinBoss)
					{
						EnemyAIManager.Instance.DisposeHordeEnemies();
						// GameDataManager.IncrementGameInt(killBossCountTrigger);
						// GameDataManager.RegisterGameTrigger(hasKilledBossTrigger, state: true);
						// int gameInt = GameDataManager.GetGameInt(killBossCountTrigger);
						// if (gameInt == 1 || gameInt == 3)
						// {
							SceneMaster.Instance.OnSceneInit += delegate
							{
								GameDirector.Instance.Player.SetInvulnerable(state: false);
							};
							GameDirector.Instance.Player.SetInvulnerable(state: true);
							SceneMaster.Instance.ReloadScene();
						// }
						// else
						// {
						// 	CombatUIManager.Instance.ShowWinScreen();
						// }
					}
				}
				else
				{
					attackController.DisposeAllAttacks();
					// GameDataManager.IncrementGameInt(killBossCountTrigger);
					// GameDataManager.RegisterGameTrigger(hasKilledBossTrigger, state: true);
					KillMidBoss();
				}
			};
			_outro.onUpdateTick = delegate
			{
			};
			_outro.onExit = delegate
			{
			};
			_phasesStateMachine.AddTransition(_intro, _phase);
			_phasesStateMachine.AddTransition(_phase, _intermission);
			_phasesStateMachine.AddTransition(_intermission, _phase);
			_phasesStateMachine.AddTransition(_intermission, _outro);
			_phasesStateMachine.AddTransition(_phase, _outro);
			_phasesStateMachine.SetInitialState(_intro);
		}

		private void FixedUpdate()
		{
			_phasesStateMachine?.FixedUpdateTick();
		}

		private void Update()
		{
			_phasesStateMachine?.UpdateTick();
		}

		private void LateUpdate()
		{
			_phasesStateMachine?.LateUpdateTick();
			_distanceToTarget = (base.transform.position - playerTransform.position).magnitude;
		}

		private void TransitionToFreakout()
		{
			attackController.TransitionToCannotAttack();
			movementController.StopMovement();
			Shooter[] array = shooters;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].StopShooting();
			}
			if (twinBoss == null)
			{
				GameDirector.Instance.Player.SetInvulnerable(state: true);
				ControllerManager.Instance.OverrideGameController<NoInputGameController>();
			}
			if ((bool)freakoutTimeline)
			{
				freakoutTimeline.stopped += delegate
				{
					GameDirector.Instance.Player.SetInvulnerable(state: false);
					ControllerManager.Instance.YieldGameController();
				};
				freakoutTimeline.Play();
			}
		}

		public void StartFreakoutAttacks()
		{
			GameEvents.Instance.OnCountDownStarted(freakoutAttacksDuration);
			attackController.ReadyToAttack();
		}

		private void TransitionToAttacking()
		{
			attackController.ExecuteAttackPattern();
		}

		public void TransitionToDead()
		{
			hurtBox.ActivateCollider(state: false);
			if (sphereAnimator != null)
			{
				sphereAnimator.Play("Minos Ball Break");
			}
			RemoveScrollingBackground();
			if ((bool)twinBoss)
			{
				twinBoss.TransitionToDead();
			}
			animator.Dead(TransitionToOutro);
		}

		private void TransitionToIntermission()
		{
			_phasesStateMachine.MakeTransition(_intermission);
		}

		public void TransitionToPhase()
		{
			_phasesStateMachine.MakeTransition(_phase);
			if ((bool)twinBoss)
			{
				twinBoss.attackController.TransitionToPhase();
			}
		}

		private void TransitionToOutro()
		{
			_phasesStateMachine.MakeTransition(_outro);
		}

		public override void Damage(Vector2 attackPosition, WeaponBehaviour weapon, DamageType damageType)
		{
			if (!base.IsImmune)
			{
				DamageInfo damageInfo = weapon.CalculateDamage(this);
				ApplyDamage(damageInfo);
				ApplyOnHitEffects(weapon, damageInfo);
				ShowDamageNumbers(damageInfo, damageType, damageNumbersPivot);
				Animator.HurtBlinkAnimation();
				sphereAnimator.Rebind();
				sphereAnimator.Play("Minos Ball Hit");
			}
		}

		public override void Damage(int value, DamageType damageType)
		{
			if (!base.IsImmune)
			{
				ApplyDamage(value);
				sphereAnimator.Rebind();
				sphereAnimator.Play("Minos Ball Hit");
				ShowDamageNumbers(-1, value, damageType, isCritical: false, damageNumbersPivot);
			}
		}

		public override void ApplyKnockBack(Vector2 attackPosition, WeaponBehaviour weaponBehaviour, bool isFatal)
		{
		}

		public override void BruteforceKnockBack(Vector2 attackPosition, KnockbackSettings settings)
		{
		}

		public override void Dispose()
		{
		}

		public override void SetImmunity(bool state)
		{
			base.SetImmunity(state);
			animator.SetImmunity(state);
		}

		public override Vector2 GetHurtBoxPosition()
		{
			return hurtBox.GetBounds().center;
		}

		public virtual void ApplyScrollingBackground()
		{
			if ((bool)backgroundAnimator)
			{
				backgroundAnimator.Play(backgroundIn.name);
			}
		}

		public virtual void RemoveScrollingBackground()
		{
			if ((bool)backgroundAnimator)
			{
				backgroundAnimator.Play(backgroundOut.name);
			}
		}

		public void SetPhaseIndex(int index)
		{
			currentPhaseIndex = index;
		}

		public void NextPhase()
		{
			if (currentPhaseIndex != phases.Length - 1)
			{
				currentPhaseIndex++;
				if ((bool)twinBoss)
				{
					twinBoss.NextPhase();
				}
			}
		}

		private bool EvaluatePhaseProgression()
		{
			if (CurrentPhaseIndex == phases.Length)
			{
				return false;
			}
			if ((float)stats.Health < phases[currentPhaseIndex].HealthThreshold)
			{
				SetImmunity(state: true);
				if (currentPhaseIndex < phases.Length - 1)
				{
					TransitionToIntermission();
				}
				else if (currentPhaseIndex == phases.Length - 1)
				{
					TransitionToIntermission();
					Debug.Log("LAST PHASE");
				}
				return true;
			}
			return false;
		}

		private void BarkLine(string conversation, int entryId)
		{
			if (BarksOn)
			{
				// Subtitle barkSubtitle = DialogueHelpers.GetBarkSubtitle(conversation, entryId, Actor.transform, base.transform);
				// AstralDialogueManager.Instance.LaunchBark(barkSubtitle);
			}
		}

		private void PauseStateMachineOnPlayerDeath()
		{
			_phasesStateMachine.Pause();
		}

		private void PlayerKilled()
		{
			// GameDataManager.RegisterGameTrigger(killPlayerTrigger, state: true);
		}

		private void OnDisable()
		{
			EnemyAIManager.Instance?.UnRegisterEnemy(this);
		}

		private void OnDestroy()
		{
			GameEvents instance = GameEvents.Instance;
			instance.OnBeforePlayerDeath = (Action)Delegate.Remove(instance.OnBeforePlayerDeath, new Action(PauseStateMachineOnPlayerDeath));
			GameEvents instance2 = GameEvents.Instance;
			instance2.OnAfterPlayerDeath = (Action)Delegate.Remove(instance2.OnAfterPlayerDeath, new Action(PlayerKilled));
		}

		private void KillMidBoss()
		{
			StopAllCoroutines();
			OnKillMidBoss.Invoke();
			mapEdges.SetActive(value: false);
			UnityEngine.Object.Destroy(base.gameObject);
		}
	}
}
