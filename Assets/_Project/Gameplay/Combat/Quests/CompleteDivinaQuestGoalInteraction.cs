using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;

namespace AstralShift.HellMaiden.Quests
{
	public class CompleteDivinaQuestGoalInteraction : Interaction
	{
		public DivinaQuestGoal quest;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			quest.Complete();
			OnEnd();
		}

		public void StopQuestTimeout()
		{
			if (quest is DivinaQuestInteractionGoal divinaQuestInteractionGoal)
			{
				divinaQuestInteractionGoal.StopQuestTimeout();
			}
		}
	}
}
