using AstralShift.HellMaiden.Quests;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;

namespace AstralShift.HellMaiden.Interactions
{
	public class GoalProgressInteraction : Interaction
	{
		public enum GoalProgressInteractionAction
		{
			Start = 0,
			Progress = 1,
			Complete = 2
		}

		public DivinaQuestGoal quest;

		public GoalProgressInteractionAction Action;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			switch (Action)
			{
			case GoalProgressInteractionAction.Start:
				GoalManager.Instance.StartQuest(quest);
				break;
			case GoalProgressInteractionAction.Progress:
				quest.Progress();
				break;
			}
			OnEnd();
		}
	}
}
