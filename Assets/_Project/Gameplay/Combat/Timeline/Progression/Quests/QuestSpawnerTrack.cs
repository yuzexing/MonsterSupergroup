using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression.Quests
{
	[TrackColor(0f, 1f, 0.8f)]
	[TrackClipType(typeof(QuestSpawnerClip))]
	[DisplayName("AstralShift/Progression/Quest Spawner Track")]
	public class QuestSpawnerTrack : TrackAsset
	{
		public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
		{
			return ScriptPlayable<QuestSpawnerTrackMixer>.Create(graph, inputCount);
		}
	}
}
