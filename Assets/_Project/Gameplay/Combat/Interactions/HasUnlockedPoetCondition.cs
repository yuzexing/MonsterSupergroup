using AstralShift.HellMaiden.Data;
using AstralShift.Initialization.Verification;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class HasUnlockedPoetCondition : Condition, IEventCondition
	{
		[SerializeField]
		private PoetPoolID poetPoolID;

		[SerializeField]
		private bool hasUnlocked = true;

		public override bool Verify(IInteractor interactor)
		{
			return VerifyCondition();
		}

		public bool VerifyCondition()
		{
			return GameDataManager.IsPoetUnlocked(poetPoolID) == hasUnlocked;
		}
	}
}
