using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public abstract class SummonAIStateModule : MonoBehaviour
	{
		protected SummonAIBehaviour _aiBehaviour;

		private Action _onComplete;

		protected bool isComplete;

		public virtual void Init(SummonAIBehaviour behaviour, Action onComplete)
		{
			_aiBehaviour = behaviour;
			isComplete = false;
			_onComplete = onComplete;
		}

		public abstract void Enter();

		public abstract void OnUpdate();

		public virtual bool IsComplete()
		{
			return isComplete;
		}

		public virtual void Exit()
		{
			isComplete = true;
			_onComplete?.Invoke();
		}
	}
}
