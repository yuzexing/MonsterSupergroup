using System.ComponentModel;
using TMPro;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.Cinematics.Timeline
{
	[TrackBindingType(typeof(TextMeshProUGUI))]
	[TrackClipType(typeof(TimelineSubtitleClip))]
	[DisplayName("AstralShift/Cinematics/Subtitles Track")]
	public class TimelineSubtitlesTrack : TrackAsset
	{
		public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
		{
			return ScriptPlayable<TimelineSubtitlesMixer>.Create(graph, inputCount);
		}

		protected override void OnCreateClip(TimelineClip clip)
		{
			base.OnCreateClip(clip);
			clip.displayName = "Subtitle";
			clip.easeInDuration = 0.20000000298023224;
			clip.easeOutDuration = 0.20000000298023224;
			clip.blendInCurveMode = TimelineClip.BlendCurveMode.Auto;
			clip.blendOutCurveMode = TimelineClip.BlendCurveMode.Auto;
		}
	}
}
