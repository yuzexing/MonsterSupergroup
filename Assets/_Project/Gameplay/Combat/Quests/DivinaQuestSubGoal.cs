using UnityEngine;

namespace AstralShift.HellMaiden.Quests
{
	public class DivinaQuestSubGoal : MonoBehaviour
	{
		public DivinaQuestGoal mainQuest;

		private bool _completed;

		public void Progress()
		{
			if (!_completed)
			{
				mainQuest.Progress();
				_completed = true;
			}
		}
	}
}
