using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression.Traps
{
	[TrackColor(0.8f, 0f, 0.8f)]
	[TrackClipType(typeof(BarrierTrapSpawnerClip))]
	[DisplayName("AstralShift/Progression/Trap Spawner Track")]
	public class TrapSpawnerTrack : TrackAsset
	{
		public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
		{
			SetClipsMinimumSize();
			return ScriptPlayable<TrapSpawnerTrackMixer>.Create(graph, inputCount);
		}

		public virtual void SetClipsMinimumSize()
		{
			foreach (TimelineClip clip in GetClips())
			{
				(clip.asset as BarrierTrapSpawnerClip).SetMinSize(clip);
			}
		}
	}
}
