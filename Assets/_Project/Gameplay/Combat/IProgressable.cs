namespace AstralShift.HellMaiden.Combat
{
	public interface IProgressable
	{
		float startTime { get; set; }

		float endTime { get; set; }

		bool progressionPaused { get; set; }

		bool hasEnded { get; set; }

		protected float Duration => endTime - startTime;

		void Init();

		void ProgressUpdate();

		void End();

		void PauseProgressable()
		{
			progressionPaused = true;
		}

		void ResumeProgressable()
		{
			progressionPaused = false;
		}
	}
}
