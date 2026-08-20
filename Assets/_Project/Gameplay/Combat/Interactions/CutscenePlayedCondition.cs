using AstralShift.HellMaiden.Data;
using AstralShift.Initialization.Verification;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class CutscenePlayedCondition : Condition, IEventCondition
	{
		[SerializeField]
		private CutsceneLUT.CutsceneLUTDependency cutscene;

		public override bool Verify(IInteractor interactor)
		{
			return VerifyCondition();
		}

		public bool VerifyCondition()
		{
			return GameDataManager.HasCutscenePlayed(cutscene.cutscene.Name) == cutscene.played;
		}
	}
}
