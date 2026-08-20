using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace AstralShift.HSM
{
	public class StateNode
	{
		public string Name { get; }

		public Func<UniTask> OnEnter { get; set; }

		public Action OnUpdate { get; set; }

		public Func<UniTask> OnUpdateAsync { get; set; }

		public Action OnFixedUpdate { get; set; }

		public Func<UniTask> OnFixedUpdateAsync { get; set; }

		public Action OnLateUpdate { get; set; }

		public Func<UniTask> OnLateUpdateAsync { get; set; }

		public Func<UniTask> OnExit { get; set; }

		public StateNode Parent { get; internal set; }

		public List<StateNode> Children { get; } = new List<StateNode>(4);

		public StateNode InitialChildState { get; internal set; }

		public StateNode(string name)
		{
			Name = name;
		}
	}
}
