using AstralShift.HellMaiden.Data;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;

namespace AstralShift.HellMaiden.Interactions
{
	public class AchievementInteraction : Interaction
	{
		public AchievementManager.AchievementID achievementID;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			GameDirector.Instance.AchievementManager.UnlockAchievement(achievementID);
			OnEnd();
		}
	}
}
