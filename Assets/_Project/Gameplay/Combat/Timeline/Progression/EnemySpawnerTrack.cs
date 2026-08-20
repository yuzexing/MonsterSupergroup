using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression
{
	[TrackColor(1f, 0f, 0f)]
	[TrackClipType(typeof(EnemySpawnerClip))]
	[TrackClipType(typeof(EnemyBurstSpawnerClip))]
	[TrackClipType(typeof(EnemyDirectionalSpawnerClip))]
	[TrackClipType(typeof(EnemyContinuousLimitedSpawnerClip))]
	[DisplayName("AstralShift/Progression/Enemy Spawner Track")]
	public class EnemySpawnerTrack : TrackAsset
	{
		public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
		{
			return ScriptPlayable<EnemySpawnerTrackMixer>.Create(graph, inputCount);
		}
	}
}
