namespace AstralShift.HellMaiden.Combat.Events
{
	public abstract class ProgressionEvent : IProgressable
	{
		public abstract float startTime { get; set; }

		public abstract float endTime { get; set; }

		public abstract bool progressionPaused { get; set; }

		public abstract bool hasEnded { get; set; }

		public abstract void Init();

		public abstract void ProgressUpdate();

		public abstract void End();
	}
}
