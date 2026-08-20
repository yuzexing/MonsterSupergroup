using AstralShift.QTI.Interactors;

namespace AstralShift.QTI.Interactions.Demos.FPPuzzleDemo
{
	public class TranslateStateCondition : Condition
	{
		public TranslateInteraction translateInteraction;

		public override bool Verify(IInteractor interactor)
		{
			return translateInteraction.IsEnabled;
		}
	}
}
