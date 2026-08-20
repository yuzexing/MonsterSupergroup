using Animancer;
using AstralShift.FSM;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class SummonAIBehaviour : MonoBehaviour
	{
		[SerializeField]
		protected AttackProgressionScaler progressionScaler;

		[SerializeField]
		private IdleStateModule idlingStateModule;

		[SerializeField]
		private PositioningStateModule positioningStateModule;

		[SerializeField]
		private AttackStateModule attackingStateModule;

		[SerializeField]
		private AnimancerComponent animancer;

		private SummonAttackBehaviour _weaponBehaviour;

		private SummonAIStateModule _currentModule;

		private StateMachine _stateMachine;

		private State _idling;

		private State _positioning;

		private State _attacking;

		private State _deactivated;

		private Transform _transform;

		public IdleStateModule IdleModule => idlingStateModule;

		public PositioningStateModule PositioningModule => positioningStateModule;

		public AttackStateModule AttackModule => attackingStateModule;

		public AnimancerComponent Animancer => animancer;

		public SummonAttackBehaviour WeaponBehaviour => _weaponBehaviour;

		public StateMachine StateMachine => _stateMachine;

		public Transform Transform => _transform ?? (_transform = base.transform);

		public void Init(SummonAttackBehaviour weapon)
		{
			_weaponBehaviour = weapon;
			UpdateProgressionScaler();
			InitModules();
			InitStateMachine();
		}

		public void UpdateProgressionScaler()
		{
			progressionScaler.Apply(_weaponBehaviour);
		}

		private void InitModules()
		{
			idlingStateModule.Init(this, CompleteIdle);
			positioningStateModule.Init(this, CompletePositioning);
			attackingStateModule.Init(this, CompleteAttack);
		}

		public void Update()
		{
			OnUpdate();
		}

		public void OnUpdate()
		{
			_stateMachine?.UpdateTick();
		}

		public void Dispose()
		{
		}

		private void InitStateMachine()
		{
			_stateMachine = new StateMachine(base.gameObject.name);
			_idling = new State("Idling");
			_idling.onEnter = OnEnterIdle;
			_idling.onUpdateTick = OnUpdateIdle;
			_idling.onExit = OnExitIdle;
			_positioning = new State("Positioning");
			_positioning.onEnter = OnEnterPositioning;
			_positioning.onUpdateTick = OnUpdatePositioning;
			_positioning.onExit = OnExitPositioning;
			_attacking = new State("Attacking");
			_attacking.onEnter = OnEnterAttack;
			_attacking.onUpdateTick = OnUpdateAttack;
			_attacking.onExit = OnExitAttack;
			_deactivated = new State("Deactivated");
			_deactivated.onEnter = OnEnterDeactivated;
			_stateMachine.AddTransition(_idling, _positioning);
			_stateMachine.AddTransition(_positioning, _attacking);
			_stateMachine.AddTransition(_attacking, _idling);
			_stateMachine.AddAnyTransition(_deactivated);
			_stateMachine.AddTransition(_deactivated, _idling);
			_stateMachine.AddTransition(_deactivated, _positioning);
			_stateMachine.AddTransition(_deactivated, _attacking);
			_stateMachine.SetInitialState(_idling);
		}

		private void OnEnterIdle()
		{
			_currentModule = idlingStateModule;
			idlingStateModule.Enter();
		}

		private void OnUpdateIdle()
		{
			idlingStateModule.OnUpdate();
		}

		private void OnExitIdle()
		{
		}

		private void CompleteIdle()
		{
			_stateMachine.MakeTransition(_positioning);
		}

		private void OnEnterPositioning()
		{
			_currentModule = positioningStateModule;
			positioningStateModule.Enter();
		}

		private void OnUpdatePositioning()
		{
			positioningStateModule.OnUpdate();
		}

		private void OnExitPositioning()
		{
		}

		private void CompletePositioning()
		{
			_stateMachine.MakeTransition(_attacking);
		}

		private void OnEnterAttack()
		{
			_currentModule = attackingStateModule;
			attackingStateModule.Enter();
		}

		private void OnUpdateAttack()
		{
			attackingStateModule.OnUpdate();
		}

		private void OnExitAttack()
		{
		}

		private void CompleteAttack()
		{
			_stateMachine.MakeTransition(_idling);
		}

		private void OnEnterDeactivated()
		{
		}

		public void Activate()
		{
			_stateMachine.MakeTransition(_idling);
		}

		public void Deactivate()
		{
			_stateMachine.MakeTransition(_deactivated);
		}
	}
}
