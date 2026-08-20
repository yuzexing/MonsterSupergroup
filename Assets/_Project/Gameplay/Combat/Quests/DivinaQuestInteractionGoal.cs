using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.MapGeneration;
using AstralShift.HellMaiden.UI.Quests;
using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace AstralShift.HellMaiden.Quests
{
	public class DivinaQuestInteractionGoal : DivinaQuestGoal
	{
		public string InteractionID;

		private float _remainingTime;

		protected override void StartQuest()
		{
			base.StartQuest();
			if (hasSpecificTile && tile != null)
			{
				LinkInteraction(tile.transform);
			}
			if (hasTimeout)
			{
				StartTimeoutTimer();
			}
		}

		public override void Complete()
		{
			if (hasTimeout)
			{
				QuestTimeoutObserver.NotifyTimeoutStopped();
			}
			DeactivatePointer();
			base.interactionParent?.gameObject.SetActive(value: false);
			base.Complete();
		}

		public override void FailQuest(FailReason failReason = FailReason.Lost)
		{
			if (base.questState != QuestState.Failure)
			{
				if (hasTimeout)
				{
					QuestTimeoutObserver.NotifyTimeoutStopped();
				}
				CameraEffects.Instance.PoetDeathScreenFlashEFX();
				DisableQuestTile();
				base.FailQuest(failReason);
			}
		}

		private void DeactivatePointer()
		{
			if (pointer != null)
			{
				MapPointerManager.Instance.ReturnPointer(pointer);
			}
		}

		private void LinkInteraction(Transform parent)
		{
			base.interactionParent = RecursiveFindChild(parent, InteractionID);
			base.interactionParent.GetComponent<CompleteDivinaQuestGoalInteraction>().quest = this;
			base.interactionParent.gameObject.SetActive(value: true);
		}

		public void DisableQuestTile()
		{
			DeactivatePointer();
			QuestMapGenerator.Instance.DisableQuestTile(this);
		}
	}
}
