using System;

namespace AstralShift.FSM
{
	public class State
	{
		public string name;

		public Action onEnter;

		public Action onEnterOnce;

		public Action onExit;

		public Action onExitOnce;

		public Action onUpdateTick;

		public Action onFixedUpdateTick;

		public Action onLateUpdateTick;

		public State(string name)
		{
			this.name = name;
		}
	}
}
