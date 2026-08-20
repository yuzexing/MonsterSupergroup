using System.Collections.Generic;
using AstralShift.FSM;
using AstralShift.Helpers;
using Unity.Behavior;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss
{
	public class BossAttackController : MonoBehaviour
	{
		public BehaviorGraphAgent behaviourAgent;

		public List<BossAttackBehaviour> attacks;

		private BossAttackPhase _currentPhase;

		private BossAttackBehaviour _currentAttack;

		protected StateMachine _stateMachine;

		protected State _deactivated;

		protected State _executingAttackPattern;

		protected State _finishedAttackPattern;

		protected State _cannotAttack;

		protected State _readyToAttack;

		protected State _intermissionTriggered;

		[SerializeField]
		private float betweenAttacksDelay = 0.5f;

		public BossController bossController { get; private set; }

		public void Init(BossController controller)
		{
			bossController = controller;
			_stateMachine = new StateMachine("Boss Attack Controller: " + base.transform.parent.name);
			_deactivated = new State("_deactivated");
			_executingAttackPattern = new State("_executingAttackPattern");
			_finishedAttackPattern = new State("_finishedAttackPattern");
			_intermissionTriggered = new State("_intermissionTriggered");
			_cannotAttack = new State("_cannotAttack");
			_readyToAttack = new State("_readyToAttack");
			_executingAttackPattern.onEnter = RunBehaviourGraph;
			_executingAttackPattern.onExit = StopBehaviourGraph;
			_intermissionTriggered.onEnter = IntermissionTriggered;
			_finishedAttackPattern.onEnter = TransitionToExecutingAttackWithDelay;
			_readyToAttack.onEnter = ExecuteAttackPattern;
			_stateMachine.AddTransition(_deactivated, _executingAttackPattern);
			_stateMachine.AddTransition(_executingAttackPattern, _finishedAttackPattern);
			_stateMachine.AddTransition(_finishedAttackPattern, _executingAttackPattern);
			_stateMachine.AddTransition(_finishedAttackPattern, _deactivated);
			_stateMachine.AddTransition(_intermissionTriggered, _finishedAttackPattern);
			_stateMachine.AddTransition(_cannotAttack, _readyToAttack);
			_stateMachine.AddTransition(_readyToAttack, _executingAttackPattern);
			_stateMachine.AddAnyTransition(_cannotAttack);
			_stateMachine.AddAnyTransition(_intermissionTriggered);
			_stateMachine.SetInitialState(_deactivated);
			for (int i = 0; i < attacks.Count; i++)
			{
				attacks[i].Init(bossController);
			}
			behaviourAgent.enabled = false;
		}

		public void Update()
		{
			_stateMachine?.UpdateTick();
		}

		public void ReadyToAttack()
		{
			_stateMachine.MakeTransition(_readyToAttack);
		}

		public void ExecuteAttackPattern()
		{
			_stateMachine.MakeTransition(_executingAttackPattern);
		}

		public void FinishAttackPattern()
		{
			_stateMachine.MakeTransition(_finishedAttackPattern);
		}

		private void TransitionToExecutingAttackWithDelay()
		{
			StartCoroutine(Wait.SetTimeout(betweenAttacksDelay, delegate
			{
				ExecuteAttackPattern();
			}));
		}

		public void RunBehaviourGraph()
		{
			behaviourAgent.enabled = true;
			behaviourAgent.Restart();
		}

		public void StopBehaviourGraph()
		{
			behaviourAgent.End();
		}

		public void TransitionToDeactivated()
		{
			_stateMachine.MakeTransition(_deactivated);
		}

		public void DisposeAllAttacks()
		{
			foreach (BossAttackBehaviour attack in attacks)
			{
				attack.Dispose();
			}
			AnimatedBossAttack[] array = Object.FindObjectsByType<AnimatedBossAttack>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
			for (int i = 0; i < array.Length; i++)
			{
				Object.Destroy(array[i].gameObject);
			}
		}

		public void TransitionToIntermission()
		{
			_stateMachine.MakeTransition(_intermissionTriggered);
		}

		private void IntermissionTriggered()
		{
			foreach (BossAttackBehaviour attack in attacks)
			{
				attack.Stop();
			}
			StopAllCoroutines();
			FinishAttackPattern();
		}

		public void TransitionToDeath()
		{
			bossController.TransitionToDead();
		}

		public void TransitionToPhase()
		{
			FinishAttackPattern();
			bossController.TransitionToPhase();
		}

		public void TransitionToCannotAttack()
		{
			foreach (BossAttackBehaviour attack in attacks)
			{
				attack.Stop();
			}
			FinishAttackPattern();
			DisposeAllAttacks();
			_stateMachine.MakeTransition(_cannotAttack);
		}
	}
}
