namespace AstralShift.HellMaiden.Combat.Events
{
	public class CircleTimeoutProgressionEvent : ProgressionEvent
	{
		public float countdownDuration;

		public override float startTime { get; set; }

		public override float endTime { get; set; }

		public override bool progressionPaused { get; set; }

		public override bool hasEnded { get; set; }

		public override void Init()
		{
		}

		public override void ProgressUpdate()
		{
			GameEvents.Instance.OnCountDownStarted?.Invoke(countdownDuration);
			hasEnded = true;
		}

		public override void End()
		{
		}
	}
}
