using System;
using System.Collections;
using System.Collections.Generic;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.UI;
using Cysharp.Threading.Tasks;
using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace AstralShift.HellMaiden.Quests
{
	public class DivinaQuestMultiTrackedGoal : DivinaQuestGoal
	{
		[Serializable]
		public struct IntercomConversation
		{
			public int objectivesToTrigger;

			// [ConversationPopup(false, false)]
			// public string conversation;
		}

		private int _numberOfObjectives;

		private int _objectivesCompleted;

		[SerializeField]
		private List<IntercomConversation> subGoalConversations = new List<IntercomConversation>();

		private Dictionary<int, string> questIntercomConversations = new Dictionary<int, string>();

		protected override void StartQuest()
		{
			// CustomQuestTracker.Instance?.RequestQuestNotification(this, questID, questIcon, createMinimapIcon);
			// questTracker = DialogueManager.Instance.GetComponentInChildren<CustomQuestTrackerTemplate>();
			_numberOfObjectives = subGoals.Length;
			_objectivesCompleted = 0;
			// DialogueLua.SetVariable("QSTTarget", _numberOfObjectives);
			questIntercomConversations = new Dictionary<int, string>();
			foreach (IntercomConversation subGoalConversation in subGoalConversations)
			{
				// questIntercomConversations.TryAdd(subGoalConversation.objectivesToTrigger, subGoalConversation.conversation);
			}
			for (int i = 0; i < subGoals.Length; i++)
			{
				subGoals[i].Init();
				subGoals[i].OnComplete = OnSubGoalComplete;
			}
			if (hasTimeout)
			{
				StartTimeoutTimer();
			}
		}

		public override void Progress()
		{
			throw new NotImplementedException();
		}

		protected override void OnSubGoalComplete()
		{
			_objectivesCompleted++;
			if (questIntercomConversations.TryGetValue(_objectivesCompleted, out var value))
			{
				IntercomManager.Instance.LaunchIntercom(value, questIntercomEntryID, AdvanceQuest, IntercomManager.MAX_PRIORITY).Forget();
			}
			else
			{
				AdvanceQuest();
			}
		}

		protected void AdvanceQuest()
		{
			// DialogueLua.SetVariable("QSTProgress", _objectivesCompleted);
			if (_objectivesCompleted == _numberOfObjectives)
			{
				Complete();
			}
			else
			{
				// CustomQuestTracker.Instance?.RequestQuestNotification(this, questID, questIcon);
			}
		}

		private void TurnOnSubGoals()
		{
			for (int i = 0; i < subGoals.Length; i++)
			{
				subGoals[i].parentQuest = this;
				subGoals[i].gameObject.SetActive(value: true);
			}
		}

		public override void StopQuestTimeout()
		{
			if (hasTimeout && _objectivesCompleted == _numberOfObjectives - 1)
			{
				if (timeoutCoroutine != null)
				{
					StopCoroutine(timeoutCoroutine);
				}
				QuestTimeoutObserver.NotifyTimeoutStopped();
			}
		}

		protected override IEnumerator CheckTimeOutFailure()
		{
			while (base.questState == QuestState.Active)
			{
				yield return new WaitForSeconds(0.5f);
				float num = ProgressionManager.Instance.StageTime - _questStartTime;
				QuestTimeoutObserver.NotifyTimeoutTick(timeout - num);
				if (!(num > timeout))
				{
					continue;
				}
				CameraEffects.Instance.PoetDeathScreenFlashEFX();
				for (int i = 0; i < subGoals.Length; i++)
				{
					if (subGoals[i] is DivinaQuestInteractionGoal divinaQuestInteractionGoal)
					{
						divinaQuestInteractionGoal.DisableQuestTile();
					}
					subGoals[i].FailQuest(FailReason.Timeout);
				}
				FailQuest(FailReason.Timeout);
				break;
			}
		}
	}
}
