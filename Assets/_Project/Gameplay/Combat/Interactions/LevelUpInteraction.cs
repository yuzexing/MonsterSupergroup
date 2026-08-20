using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;

namespace AstralShift.HellMaiden.Interactions
{
	public class LevelUpInteraction : Interaction
	{
		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			Leveler.Instance.IncreaseLevel();
			OnEnd();
		}
	}
}
