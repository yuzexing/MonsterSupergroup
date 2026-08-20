using System.Collections.Generic;
using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace AstralShift.HellMaiden.Quests
{
	public class GoalManager : MonoBehaviour
	{
		public static GoalManager Instance;

		private Dictionary<string, DivinaQuestGoal> _goals;

		private void Awake()
		{
			if (Instance == null)
			{
				Instance = this;
			}
			else
			{
				Object.Destroy(this);
			}
		}

		private void Start()
		{
			_goals = new Dictionary<string, DivinaQuestGoal>();
		}

		public void StartQuest(DivinaQuestGoal quest)
		{
			quest.Init();
			// QuestLog.StartQuest(quest.questID);
			// _goals.Add(quest.questID, quest);
		}

		public void CompleteQuest(string QuestID)
		{
			// QuestLog.CompleteQuest(QuestID);
			_goals.Remove(QuestID);
		}

		public void ProgressQuest(string QuestID)
		{
			_goals[QuestID].Progress();
		}
	}
}
