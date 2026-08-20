using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Spawners;
using AstralShift.HellMaiden.Scenes;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression.Boss
{
	public class BossSpawnerClip : SpritePlayableAsset, IProgressionClip
	{
		private BossSpawnerBehaviour template = new BossSpawnerBehaviour();

		public SceneEnum bossScene;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
		{
			return ScriptPlayable<BossSpawnerBehaviour>.Create(graph, template);
		}

		public void ProcessClip(ProgressionTimeline timeline, TimelineClip clip)
		{
			BossSpawner bossSpawner = Object.Instantiate(timeline.bossSpawner, timeline.transform);
			bossSpawner.BossScene = bossScene;
			timeline.CreateMilestone(bossSpawner, clip);
		}
	}
}
