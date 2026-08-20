using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Spawners;
using AstralShift.HellMaiden.Combat.Traps;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression.Traps
{
	public class BarrierTrapSpawnerClip : SpritePlayableAsset, ITimelineClipAsset, IProgressionClip
	{
		private TrapSpawnerBehaviour template = new TrapSpawnerBehaviour();

		public BarrierTrap trapPrefab;

		public ClipCaps clipCaps => ClipCaps.None;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
		{
			return ScriptPlayable<TrapSpawnerBehaviour>.Create(graph, template);
		}

		public void SetMinSize(TimelineClip clip)
		{
			float num = trapPrefab.ShrinkDuration + 0.01f;
			if (clip.duration < (double)num)
			{
				clip.duration = num;
			}
		}

		public void ProcessClip(ProgressionTimeline timeline, TimelineClip clip)
		{
			BarrierTrapSpawner barrierTrapSpawner = Object.Instantiate(timeline.barrierTrapSpawner, timeline.transform);
			barrierTrapSpawner.trap = trapPrefab;
			timeline.CreateMilestone(barrierTrapSpawner, clip);
		}
	}
}
