using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Spawners;
using UnityEngine;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression
{
	public class EnemyContinuousWaveDefenceSpawnerClip : EnemyContinuousLimitedSpawnerClip
	{
		public override void ProcessClip(ProgressionTimeline timeline, TimelineClip clip)
		{
			ContinuousWaveDefenceSpawner continuousWaveDefenceSpawner = Object.Instantiate(timeline.enemyContinuousWaveDefenceSpawner, timeline.transform);
			continuousWaveDefenceSpawner.enemyCount = (int)enemyAmount;
			continuousWaveDefenceSpawner.spawnCooldown = (int)spawnCoolDown;
			continuousWaveDefenceSpawner.enemyPrefab = enemyPrefab;
			continuousWaveDefenceSpawner.variantIdx = variantIndex;
			continuousWaveDefenceSpawner.onlyFinishOnDeath = onlyFinishOnDeath;
			timeline.CreateMilestone(continuousWaveDefenceSpawner, clip);
		}
	}
}
