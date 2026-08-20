using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Spawners;
using AstralShift.HellMaiden.Common;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression
{
	public class EnemyContinuousLimitedSpawnerClip : EnemyClip
	{
		private EnemySpawnerBehaviour template = new EnemySpawnerBehaviour();

		public uint enemyAmount = 10u;

		public uint spawnCoolDown = 4u;

		public EnemyController enemyPrefab;

		public bool onlyFinishOnDeath;

		public Direction direction;

		public float angle;

		public ClipCaps clipCaps => ClipCaps.None;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
		{
			ScriptPlayable<EnemySpawnerBehaviour> scriptPlayable = ScriptPlayable<EnemySpawnerBehaviour>.Create(graph, template);
			if (enemyPrefab != null)
			{
				sprite = enemyPrefab.spriteRenderer.sprite;
			}
			return scriptPlayable;
		}

		public override void ProcessClip(ProgressionTimeline timeline, TimelineClip clip)
		{
			ContinuousLimitedEnemySpawner continuousLimitedEnemySpawner = Object.Instantiate(timeline.enemyContinuousLimitedSpawner, timeline.transform);
			continuousLimitedEnemySpawner.enemyCount = (int)enemyAmount;
			continuousLimitedEnemySpawner.spawnCooldown = (int)spawnCoolDown;
			continuousLimitedEnemySpawner.enemyPrefab = enemyPrefab;
			continuousLimitedEnemySpawner.variantIdx = variantIndex;
			continuousLimitedEnemySpawner.onlyFinishOnDeath = onlyFinishOnDeath;
			continuousLimitedEnemySpawner.direction = direction;
			continuousLimitedEnemySpawner.angle = angle;
			timeline.CreateMilestone(continuousLimitedEnemySpawner, clip);
		}
	}
}
