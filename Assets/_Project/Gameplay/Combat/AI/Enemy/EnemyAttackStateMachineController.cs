using AstralShift.FSM;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyAttackStateMachineController : EnemyAttackPrefab
	{
		protected StateMachine _stateMachine;

		public State Charging;

		public State Fired;

		public State Hit;

		public State Expire;

		public State End;

		public StateMachine StateMachine => _stateMachine;

		protected virtual void InitializeStateMachine()
		{
			_stateMachine = new StateMachine("EnemyBullet");
			Charging = new State("Charging");
			Fired = new State("Fired");
			Hit = new State("Hit");
			Expire = new State("Expire");
			End = new State("End");
			_stateMachine.AddTransition(Charging, Fired);
			_stateMachine.AddTransition(Fired, Hit);
			_stateMachine.AddTransition(Fired, Expire);
			_stateMachine.AddTransition(Expire, Fired);
			_stateMachine.AddTransition(Hit, End);
			_stateMachine.AddTransition(Hit, Expire);
			_stateMachine.AddTransition(Expire, End);
			_stateMachine.AddTransition(Fired, Expire);
			_stateMachine.AddAnyTransition(End);
		}

		public void TransitionToEnd()
		{
			_stateMachine?.MakeTransition(End);
		}

		protected void TransitionToHit()
		{
			_stateMachine?.MakeTransition(Hit);
		}

		protected void TransitionToFire()
		{
			_stateMachine?.MakeTransition(Fired);
		}

		public void TransitionToExpire()
		{
			_stateMachine?.MakeTransition(Expire);
		}
	}
}
