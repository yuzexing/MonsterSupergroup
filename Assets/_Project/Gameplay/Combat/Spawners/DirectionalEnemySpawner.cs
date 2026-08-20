using AstralShift.HellMaiden.AI.Enemy;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Spawners
{
	public class DirectionalEnemySpawner : EnemySpawner
	{
		public float offscreenTimeoutDistance = 1.5f;

		public override void Init()
		{
			SetupPools(enemyCount);
			for (int i = 0; i < enemyCount; i++)
			{
				if (GetSpawnPosition(out var spawnPosition))
				{
					SpawnEnemy(spawnPosition);
				}
			}
			base.hasEnded = true;
		}

		protected override EnemyController SpawnEnemy(Vector2 spawnPosition)
		{
			EnemyController enemyController = base.SpawnEnemy(spawnPosition);
			enemyController.allowRubberband = false;
			return enemyController;
		}

		public override void ProgressUpdate()
		{
		}

		public override void End()
		{
		}
	}
}
