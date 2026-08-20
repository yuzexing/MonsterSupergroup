using System;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.Cinematics
{
	[Serializable]
	[TrackClipType(typeof(VideoScriptPlayableAsset))]
	[TrackColor(0.008f, 0.698f, 0.655f)]
	[DisplayName("AstralShift/Cinematics/Video Player Track")]
	public class VideoScriptPlayableTrack : TrackAsset
	{
		public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
		{
			PlayableDirector component = go.GetComponent<PlayableDirector>();
			ScriptPlayable<VideoSchedulerPlayableBehaviour> scriptPlayable = ScriptPlayable<VideoSchedulerPlayableBehaviour>.Create(graph, inputCount);
			VideoSchedulerPlayableBehaviour behaviour = scriptPlayable.GetBehaviour();
			if (behaviour != null)
			{
				behaviour.director = component;
				behaviour.clips = GetClips();
			}
			return scriptPlayable;
		}
	}
}
