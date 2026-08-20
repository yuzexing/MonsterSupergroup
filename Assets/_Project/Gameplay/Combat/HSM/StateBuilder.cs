using System;
using Cysharp.Threading.Tasks;

namespace AstralShift.HSM
{
	public class StateBuilder
	{
		private readonly StateMachine _machine;

		private readonly StateNode _state;

		public StateBuilder(StateMachine machine, StateNode state)
		{
			_machine = machine;
			_state = state;
		}

		public StateBuilder SetOnEnter(Func<UniTask> onEnter = null)
		{
			_state.OnEnter = onEnter;
			return this;
		}

		public StateBuilder SetOnExit(Func<UniTask> onExit = null)
		{
			_state.OnExit = onExit;
			return this;
		}

		public StateBuilder SetUpdate(Action onUpdate = null, UpdateType updateType = UpdateType.Update)
		{
			switch (updateType)
			{
			case UpdateType.Update:
				_state.OnUpdate = onUpdate;
				break;
			case UpdateType.FixedUpdate:
				_state.OnFixedUpdate = onUpdate;
				break;
			case UpdateType.LateUpdate:
				_state.OnLateUpdate = onUpdate;
				break;
			}
			return this;
		}

		public StateBuilder SetAsyncUpdate(Func<UniTask> onUpdateAsync = null, UpdateType updateType = UpdateType.Update)
		{
			switch (updateType)
			{
			case UpdateType.Update:
				_state.OnUpdateAsync = onUpdateAsync;
				break;
			case UpdateType.FixedUpdate:
				_state.OnFixedUpdateAsync = onUpdateAsync;
				break;
			case UpdateType.LateUpdate:
				_state.OnLateUpdateAsync = onUpdateAsync;
				break;
			}
			return this;
		}

		public StateBuilder AddSubState(string name, out StateNode childRef, Action<StateBuilder> childConfig = null)
		{
			childRef = _machine.CreateOrGetState(name, _state);
			StateBuilder obj = new StateBuilder(_machine, childRef);
			childConfig?.Invoke(obj);
			return this;
		}

		public StateBuilder SetAsInitial()
		{
			_machine.SetInitialState(_state, _state.Parent);
			return this;
		}
	}
}
