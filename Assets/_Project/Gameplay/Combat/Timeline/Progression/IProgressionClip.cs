using AstralShift.HellMaiden.Combat;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression
{
	public interface IProgressionClip
	{
		void ProcessClip(ProgressionTimeline timeline, TimelineClip clip);
	}
}
