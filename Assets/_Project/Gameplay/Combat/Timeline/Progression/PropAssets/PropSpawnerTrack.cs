using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression.PropAssets
{
	[TrackColor(0.7f, 0.4f, 0f)]
	[TrackClipType(typeof(PropSpawnerClip))]
	[DisplayName("AstralShift/Progression/Prop Spawner Track")]
	public class PropSpawnerTrack : TrackAsset
	{
		public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
		{
			return ScriptPlayable<PropSpawnerTrackMixer>.Create(graph, inputCount);
		}
	}
}
