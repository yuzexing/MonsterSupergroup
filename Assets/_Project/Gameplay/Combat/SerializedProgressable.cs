using UnityEngine;

namespace AstralShift.HellMaiden.Combat
{
	public abstract class SerializedProgressable : MonoBehaviour, IProgressable
	{
		public float startTime { get; set; }

		public float endTime { get; set; }

		public bool progressionPaused { get; set; }

		public bool hasEnded { get; set; }

		protected float Duration => endTime - startTime;

		public abstract void Init();

		public abstract void ProgressUpdate();

		public abstract void End();

		public virtual void PauseProgressable()
		{
			progressionPaused = true;
		}

		public virtual void ResumeProgressable()
		{
			progressionPaused = false;
		}
	}
}
