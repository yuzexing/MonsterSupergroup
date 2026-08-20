using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline
{
	public abstract class AstralTrackAsset : TrackAsset
	{
		protected virtual void SetClipsMinimumSize()
		{
			foreach (TimelineClip clip in GetClips())
			{
				if (clip.duration < 0.5)
				{
					clip.duration = 0.5;
				}
			}
		}
	}
}
