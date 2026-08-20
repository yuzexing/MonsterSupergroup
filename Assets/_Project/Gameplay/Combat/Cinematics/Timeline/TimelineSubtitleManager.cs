using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.Cinematics.Timeline
{
	[RequireComponent(typeof(PlayableDirector))]
	public class TimelineSubtitleManager : MonoBehaviour
	{
		[SerializeField]
		private TimelineSubtitlesData timelineSubtitlesData;

		private int index;

		private void Awake()
		{
			if (timelineSubtitlesData == null)
			{
				return;
			}
			PlayableDirector component = GetComponent<PlayableDirector>();
			foreach (TimelineClip clip in ((TimelineAsset)component.playableAsset).GetRootTrack(1).GetClips())
			{
				_ = (TimelineSubtitleClip)clip.asset;
				_ = timelineSubtitlesData.LUT[index];
				index++;
			}
			component.RebuildGraph();
		}
	}
}
