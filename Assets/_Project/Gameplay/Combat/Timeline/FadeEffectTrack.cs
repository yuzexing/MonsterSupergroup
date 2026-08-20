using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.Timeline
{
	[TrackColor(0.2f, 0.8f, 0.2f)]
	[TrackClipType(typeof(FadeEffectClip))]
	[TrackBindingType(typeof(Image))]
	[DisplayName("AstralShift/Cutscenes/Fade Effect Track")]
	public class FadeEffectTrack : AstralTrackAsset
	{
		public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
		{
			go.GetComponent<PlayableDirector>();
			ScriptPlayable<FadeEffectTrackMixer> scriptPlayable = ScriptPlayable<FadeEffectTrackMixer>.Create(graph, inputCount);
			foreach (TimelineClip clip in GetClips())
			{
				_ = (FadeEffectClip)clip.asset;
			}
			SetClipsMinimumSize();
			return scriptPlayable;
		}
	}
}
