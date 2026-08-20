using AstralShift.HellMaiden.CameraFX;
using UnityEngine;

namespace AstralShift.HellMaiden.Quests
{
	public class DivinaKillObjectiveQuestGoal : DivinaQuestGoal
	{
		private KillObjectiveQuest _killObjectiveQuest;

		public string InteractionID;

		protected override void StartQuest()
		{
			base.StartQuest();
			CameraEffects.Instance.EndWarning();
			if (hasSpecificTile && tile != null)
			{
				LinkInteraction(tile.transform);
			}
		}

		public override void Complete()
		{
			_killObjectiveQuest?.gameObject.SetActive(value: false);
			base.Complete();
		}

		private void LinkInteraction(Transform parent)
		{
			_killObjectiveQuest = RecursiveFindChild(parent, InteractionID).GetComponent<KillObjectiveQuest>();
			_killObjectiveQuest.QuestGoal = this;
			_killObjectiveQuest.gameObject.SetActive(value: true);
		}
	}
}
