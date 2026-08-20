using System;
using System.Collections.Generic;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat
{
	public class ProgressionManagerCondition : Condition
	{
		[Serializable]
		public class GameLogicCondition
		{
			public GameLogicConditionType type;

			public bool negate;
		}

		public enum GameLogicConditionType
		{
			TrapsCurrentRunning = 1
		}

		[SerializeField]
		private List<GameLogicCondition> _conditions;

		public override bool Verify(IInteractor interactor)
		{
			for (int i = 0; i < _conditions.Count; i++)
			{
				if (!EvaluateCondition(_conditions[i]))
				{
					return false;
				}
			}
			return true;
		}

		private bool EvaluateCondition(GameLogicCondition condition)
		{
			bool flag = true;
			if (condition.type == GameLogicConditionType.TrapsCurrentRunning)
			{
				flag = ProgressionManager.Instance.TrapCount > 0;
			}
			return condition.negate ? (!flag) : flag;
		}
	}
}
