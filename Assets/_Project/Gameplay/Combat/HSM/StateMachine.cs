using System;
using System.Collections.Generic;
using AstralShift.DebugTools;
using Cysharp.Threading.Tasks;

namespace AstralShift.HSM
{
	public class StateMachine
	{
		private class Transition
		{
			public StateNode To { get; }

			public Func<UniTask> OnTransition { get; }

			public Transition(StateNode to, Func<UniTask> onTransition = null)
			{
				To = to;
				OnTransition = onTransition;
			}
		}

		private readonly string _ownerName;

		private readonly bool _hasLogs;

		private readonly Dictionary<string, StateNode> _statesByName = new Dictionary<string, StateNode>();

		private readonly Dictionary<StateNode, List<Transition>> _transitions = new Dictionary<StateNode, List<Transition>>();

		private readonly List<Transition> _anyTransitions = new List<Transition>(8);

		private readonly List<StateNode> _activeExecutionPath = new List<StateNode>(8);

		private readonly List<StateNode> _entryPathCache = new List<StateNode>(8);

		private StateNode _rootState;

		private StateNode _currentState;

		private bool _isPaused;

		private bool _isTransitioning;

		public StateNode CurrentState => _currentState;

		public StateNode PreviousState { get; private set; }

		public bool IsTransitioning => _isTransitioning;

		public StateMachine(string ownerName, bool logs = true)
		{
			_ownerName = ownerName;
			_hasLogs = logs;
		}

		public StateBuilder CreateState(string name, out StateNode stateRef)
		{
			stateRef = CreateOrGetState(name, null);
			return new StateBuilder(this, stateRef);
		}

		internal StateNode CreateOrGetState(string name, StateNode parent)
		{
			if (!_statesByName.TryGetValue(name, out var value))
			{
				value = new StateNode(name);
				_statesByName[name] = value;
			}
			if (parent != null)
			{
				value.Parent = parent;
				if (!parent.Children.Contains(value))
				{
					parent.Children.Add(value);
				}
			}
			else if (_rootState == null)
			{
				_rootState = value;
			}
			return value;
		}

		public void SetInitialState(StateNode state, StateNode parent = null)
		{
			if (parent == null)
			{
				_rootState = state;
				if (_currentState == null)
				{
					ChangeStateSequenceForget(state);
				}
			}
			else
			{
				parent.InitialChildState = state;
			}
		}

		public void CreateTransition(StateNode from, StateNode to, Func<UniTask> onTransition = null)
		{
			if (!_transitions.TryGetValue(from, out var value))
			{
				value = new List<Transition>(4);
				_transitions[from] = value;
			}
			value.Add(new Transition(to, onTransition));
		}

		public void UpdateTick()
		{
			if (_isPaused || _currentState == null || _isTransitioning)
			{
				return;
			}
			int count = _activeExecutionPath.Count;
			for (int i = 0; i < count; i++)
			{
				StateNode stateNode = _activeExecutionPath[i];
				stateNode.OnUpdate?.Invoke();
				if (stateNode.OnUpdateAsync != null)
				{
					stateNode.OnUpdateAsync().Forget();
				}
			}
		}

		public void FixedUpdateTick()
		{
			if (_isPaused || _currentState == null || _isTransitioning)
			{
				return;
			}
			int count = _activeExecutionPath.Count;
			for (int i = 0; i < count; i++)
			{
				StateNode stateNode = _activeExecutionPath[i];
				stateNode.OnFixedUpdate?.Invoke();
				if (stateNode.OnFixedUpdateAsync != null)
				{
					stateNode.OnFixedUpdateAsync().Forget();
				}
			}
		}

		public void LateUpdateTick()
		{
			if (_isPaused || _currentState == null || _isTransitioning)
			{
				return;
			}
			int count = _activeExecutionPath.Count;
			for (int i = 0; i < count; i++)
			{
				StateNode stateNode = _activeExecutionPath[i];
				stateNode.OnLateUpdate?.Invoke();
				if (stateNode.OnLateUpdateAsync != null)
				{
					stateNode.OnLateUpdateAsync().Forget();
				}
			}
		}

		public async UniTask MakeTransitionAsync(StateNode toState)
		{
			if (_isPaused || toState == null || _currentState == toState || _isTransitioning)
			{
				return;
			}
			if (IsValidTransition(toState, out var matchingTransition))
			{
				if (_hasLogs)
				{
					DBL.Log(DBL.Module.FSM, _ownerName + ": Manual Transition to -> " + toState.Name);
				}
				_isTransitioning = true;
				if (matchingTransition != null && matchingTransition.OnTransition != null)
				{
					await matchingTransition.OnTransition();
				}
				await ChangeStateSequenceAsync(toState);
				_isTransitioning = false;
			}
			else
			{
				DBL.Log(DBL.Module.FSM, _ownerName + ": INVALID TRANSITION REQUEST from " + _currentState?.Name + " to " + toState.Name, 1);
			}
		}

		private bool IsValidTransition(StateNode toState, out Transition matchingTransition)
		{
			int count = _anyTransitions.Count;
			for (int i = 0; i < count; i++)
			{
				if (_anyTransitions[i].To == toState)
				{
					matchingTransition = _anyTransitions[i];
					return true;
				}
			}
			if (_currentState != null && _transitions.TryGetValue(_currentState, out var value))
			{
				int count2 = value.Count;
				for (int j = 0; j < count2; j++)
				{
					if (value[j].To == toState)
					{
						matchingTransition = value[j];
						return true;
					}
				}
			}
			for (StateNode stateNode = _currentState?.Parent; stateNode != null; stateNode = stateNode.Parent)
			{
				if (_transitions.TryGetValue(stateNode, out var value2))
				{
					int count3 = value2.Count;
					for (int k = 0; k < count3; k++)
					{
						if (value2[k].To == toState)
						{
							matchingTransition = value2[k];
							return true;
						}
					}
				}
			}
			matchingTransition = null;
			return false;
		}

		private async UniTask ChangeStateSequenceAsync(StateNode targetState)
		{
			PreviousState = _currentState;
			StateNode leastCommonAncestor = FindLeastCommonAncestor(_currentState, targetState);
			StateNode exitNode = _currentState;
			while (exitNode != null && exitNode != leastCommonAncestor)
			{
				if (exitNode.OnExit != null)
				{
					await exitNode.OnExit();
				}
				exitNode = exitNode.Parent;
			}
			_entryPathCache.Clear();
			StateNode stateNode = targetState;
			while (stateNode != null && stateNode != leastCommonAncestor)
			{
				_entryPathCache.Add(stateNode);
				stateNode = stateNode.Parent;
			}
			for (int i = _entryPathCache.Count - 1; i >= 0; i--)
			{
				if (_entryPathCache[i].OnEnter != null)
				{
					await _entryPathCache[i].OnEnter();
				}
			}
			_currentState = targetState;
			while (_currentState.InitialChildState != null)
			{
				_currentState = _currentState.InitialChildState;
				if (_currentState.OnEnter != null)
				{
					await _currentState.OnEnter();
				}
			}
			_activeExecutionPath.Clear();
			for (StateNode stateNode2 = _currentState; stateNode2 != null; stateNode2 = stateNode2.Parent)
			{
				_activeExecutionPath.Insert(0, stateNode2);
			}
		}

		private void ChangeStateSequenceForget(StateNode targetState)
		{
			ChangeStateSequenceAsync(targetState).Forget();
		}

		private StateNode FindLeastCommonAncestor(StateNode a, StateNode b)
		{
			if (a == null || b == null)
			{
				return null;
			}
			HashSet<StateNode> hashSet = new HashSet<StateNode>();
			while (a != null)
			{
				hashSet.Add(a);
				a = a.Parent;
			}
			while (b != null)
			{
				if (hashSet.Contains(b))
				{
					return b;
				}
				b = b.Parent;
			}
			return null;
		}
	}
}
