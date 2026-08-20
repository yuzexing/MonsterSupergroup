using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression.Shrines
{
	[TrackColor(0.3f, 0.4f, 0.8f)]
	[TrackClipType(typeof(ShrineSpawnerClip))]
	[DisplayName("AstralShift/Progression/Shrine Spawner Track")]
	public class ShrineSpawnerTrack : TrackAsset
	{
		public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
		{
			return ScriptPlayable<ShrineSpawnerTrackMixer>.Create(graph, inputCount);
		}
	}
}
