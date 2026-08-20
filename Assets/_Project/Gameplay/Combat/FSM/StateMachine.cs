using System;
using System.Collections.Generic;
using AstralShift.DebugTools;

namespace AstralShift.FSM
{
	public class StateMachine
	{
		private class Transition
		{
			public Action OnTransition;

			public Func<bool> Condition { get; }

			public State To { get; }

			public Transition(State to, Func<bool> condition, Action onTransition = null)
			{
				To = to;
				Condition = condition;
				OnTransition = onTransition;
			}
		}

		private State _initialState;

		private State _currentState;

		private List<State> _states;

		private bool _isPaused;

		private Dictionary<string, List<Transition>> _transitions = new Dictionary<string, List<Transition>>();

		private List<Transition> _currentTransitions = new List<Transition>();

		private List<Transition> _anyTransitions = new List<Transition>();

		private static List<Transition> EmptyTransitions = new List<Transition>(0);

		private string _ownerName;

		private bool _hasLogs;

		public IReadOnlyList<State> States => _states;

		public State PreviousState { get; private set; }

		public StateMachine(string ownerName, bool logs = true)
		{
			_ownerName = ownerName;
			_hasLogs = logs;
			_states = new List<State>();
		}

		public void Reset(bool clearStateOnceCallbacks = true)
		{
			if (clearStateOnceCallbacks)
			{
				foreach (State state in _states)
				{
					state.onEnterOnce = null;
					state.onExitOnce = null;
				}
			}
			PreviousState = null;
			SetState(_initialState);
		}

		public void UpdateTick()
		{
			if (!_isPaused)
			{
				_currentState?.onUpdateTick?.Invoke();
			}
		}

		public void FixedUpdateTick()
		{
			if (!_isPaused)
			{
				_currentState?.onFixedUpdateTick?.Invoke();
			}
		}

		public void LateUpdateTick()
		{
			if (!_isPaused)
			{
				_currentState?.onLateUpdateTick?.Invoke();
			}
		}

		public void SetInitialState(State state)
		{
			if (_currentState == null)
			{
				_initialState = state;
				SetState(state);
			}
		}

		public void SetInitialStateNoCallbacks(State state)
		{
			if (_currentState == null)
			{
				_currentState = state;
				_initialState = state;
				_transitions.TryGetValue(_currentState.name, out _currentTransitions);
				if (_currentTransitions == null)
				{
					_currentTransitions = EmptyTransitions;
				}
			}
			else
			{
				DBL.Log(DBL.Module.FSM, "Can't set initial state: a state is already set!", 1);
			}
		}

		private void SetState(State state)
		{
			if (state != _currentState)
			{
				PreviousState = _currentState;
				if (_currentState != null)
				{
					_currentState.onExit?.Invoke();
					_currentState.onExitOnce?.Invoke();
					_currentState.onExitOnce = null;
				}
				_currentState = state;
				_transitions.TryGetValue(_currentState.name, out _currentTransitions);
				if (_currentTransitions == null)
				{
					_currentTransitions = EmptyTransitions;
				}
				_currentState.onEnter?.Invoke();
				state.onEnterOnce?.Invoke();
				state.onEnterOnce = null;
			}
		}

		public void MakeTransition(State to)
		{
			if (_currentState.name == to.name || _isPaused)
			{
				return;
			}
			if (_anyTransitions.Find((Transition t) => t.To == to) != null || _currentTransitions.Find((Transition t) => t.To == to) != null)
			{
				if (_hasLogs)
				{
					DBL.Log(DBL.Module.FSM, _ownerName + ": " + _currentState.name + " -> TRANSITION to: " + to.name);
				}
				SetState(to);
			}
			else
			{
				DBL.Log(DBL.Module.FSM, _ownerName + ": " + _currentState.name + " -> INVALID TRANSITION to: " + to.name, 1);
			}
		}

		public void AddTransition(State from, State to, Func<bool> predicate, Action onTransition = null)
		{
			if (!_transitions.TryGetValue(from.name, out var value))
			{
				value = new List<Transition>();
				_transitions[from.name] = value;
			}
			value.Add(new Transition(to, predicate, onTransition));
			if (!_states.Contains(from))
			{
				_states.Add(from);
			}
			if (!_states.Contains(to))
			{
				_states.Add(to);
			}
		}

		public void AddTransition(State from, State to, Action onTransition = null)
		{
			if (!_transitions.TryGetValue(from.name, out var value))
			{
				value = new List<Transition>();
				_transitions[from.name] = value;
			}
			value.Add(new Transition(to, () => true, onTransition));
			if (!_states.Contains(from))
			{
				_states.Add(from);
			}
			if (!_states.Contains(to))
			{
				_states.Add(to);
			}
		}

		public void AddAnyTransition(State state, Func<bool> predicate, Action onTransition = null)
		{
			_anyTransitions.Add(new Transition(state, predicate, onTransition));
			if (!_states.Contains(state))
			{
				_states.Add(state);
			}
		}

		public void AddAnyTransition(State state, Action onTransition = null)
		{
			_anyTransitions.Add(new Transition(state, () => true, onTransition));
			if (!_states.Contains(state))
			{
				_states.Add(state);
			}
		}

		private Transition GetTransition()
		{
			foreach (Transition anyTransition in _anyTransitions)
			{
				if (anyTransition.Condition())
				{
					return anyTransition;
				}
			}
			foreach (Transition currentTransition in _currentTransitions)
			{
				if (currentTransition.Condition())
				{
					return currentTransition;
				}
			}
			return null;
		}

		public State GetState()
		{
			return _currentState;
		}

		public State[] GetStates()
		{
			return _states.ToArray();
		}

		public void Pause()
		{
			_isPaused = true;
		}

		public void UnPause()
		{
			_isPaused = false;
		}
	}
}
