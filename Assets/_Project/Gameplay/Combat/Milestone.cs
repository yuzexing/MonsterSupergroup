using System;

namespace AstralShift.HellMaiden.Combat
{
	[Serializable]
	public class Milestone
	{
		public float startTime;

		public float endTime;

		public int updateInterval;

		public IProgressable progressable;

		public float lastUpdate { get; set; }

		public Milestone(float startTime, float endTime, IProgressable progressable, int updateInterval = 30)
		{
			this.startTime = startTime;
			this.endTime = endTime;
			this.progressable = progressable;
			this.updateInterval = updateInterval;
		}
	}
}
