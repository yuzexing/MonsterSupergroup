using System;

namespace AstralShift.HellMaiden.Quests
{
	public static class QuestTimeoutObserver
	{
		public static event Action<float> OnAnyQuestTimeoutStarted;

		public static event Action OnAnyQuestTimeoutStopped;

		public static event Action<float> OnAnyQuestTimeoutTick;

		public static void NotifyTimeoutStarted(float duration)
		{
			QuestTimeoutObserver.OnAnyQuestTimeoutStarted?.Invoke(duration);
		}

		public static void NotifyTimeoutTick(float seconds)
		{
			QuestTimeoutObserver.OnAnyQuestTimeoutTick?.Invoke(seconds);
		}

		public static void NotifyTimeoutStopped()
		{
			QuestTimeoutObserver.OnAnyQuestTimeoutStopped?.Invoke();
		}
	}
}
