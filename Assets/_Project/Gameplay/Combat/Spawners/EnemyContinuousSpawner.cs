using System.Collections;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Common;
using AstralShift.HellMaiden.Helpers;
using AstralShift.Managers;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Spawners
{
	public class EnemyContinuousSpawner : EnemySpawner, IPausable
	{
		protected Coroutine spawnRoutine;

		private WaitForSeconds spawnIntervalYield;

		private bool _isPaused;

		public AnimationCurve spawnCurve;

		private int currentIndex;

		private List<float> spawnTimestamps;

		public override void Init()
		{
			SetupPools(enemyCount);
			((IPausable)this).Subscribe();
			spawnTimestamps = GenerateSpawnTimestamps();
			spawnRoutine = StartCoroutine(SpawnRoutine());
		}

		public virtual IEnumerator SpawnRoutine()
		{
			while (true)
			{
				if (_isPaused)
				{
					yield return null;
				}
				if (!ProgressionManager.Instance.ReachedMaxEnemiesCount)
				{
					yield return new WaitWhile(() => base.progressionPaused);
					if (GetSpawnPosition(out var spawnPosition))
					{
						SpawnEnemy(spawnPosition);
					}
					if (currentIndex < spawnTimestamps.Count - 1 && !ProgressionManager.Instance.ReachedMaxEnemiesCount)
					{
						currentIndex++;
						float seconds = spawnTimestamps[currentIndex] - spawnTimestamps[currentIndex - 1];
						spawnIntervalYield = new WaitForSeconds(seconds);
					}
					else
					{
						base.hasEnded = true;
					}
				}
				else
				{
					base.hasEnded = true;
				}
				yield return spawnIntervalYield;
			}
		}

		protected override bool GetSpawnPosition(out Vector2 spawnPosition)
		{
			if (direction == Direction.None)
			{
				if (!SpawnHelpers.GetOffScreenSpawnPosition(enemyPrefab.spawnReferenceRadius, enemyPrefab.LocalBounds, distanceFromCamera, obstaclesLayerMask, 100, out spawnPosition))
				{
					return false;
				}
			}
			else if (!SpawnHelpers.GetOffScreenSpawnPositionInDirection(enemyPrefab.spawnReferenceRadius, enemyPrefab.LocalBounds, distanceFromCamera, obstaclesLayerMask, direction.ToVector2(), out spawnPosition, angle))
			{
				return false;
			}
			return true;
		}

		protected override EnemyController SpawnEnemy(Vector2 spawnPosition)
		{
			EnemyController enemyController = base.SpawnEnemy(spawnPosition);
			if (direction != Direction.None)
			{
				enemyController.direction = direction;
				enemyController.angle = angle;
			}
			return enemyController;
		}

		private List<float> GenerateSpawnTimestamps()
		{
			List<float> list = new List<float>();
			int num = 100;
			float[] array = new float[num + 1];
			float num2 = 0f;
			for (int i = 0; i <= num; i++)
			{
				float time = (float)i / (float)num;
				float num3 = spawnCurve.Evaluate(time);
				if (i > 0)
				{
					num2 += num3 / (float)num;
				}
				array[i] = num2;
			}
			for (int j = 0; j <= num; j++)
			{
				array[j] *= (float)enemyCount / num2;
			}
			int k = 0;
			for (int l = 1; l <= num; l++)
			{
				for (; (float)k < array[l]; k++)
				{
					float t = ((float)k - array[l - 1]) / (array[l] - array[l - 1]);
					float item = Mathf.Lerp((float)(l - 1) * base.Duration / (float)num, (float)l * base.Duration / (float)num, t);
					list.Add(item);
				}
			}
			return list;
		}

		public override void ProgressUpdate()
		{
		}

		private void UpdateSpawnIntervals()
		{
		}

		public override void End()
		{
			StopCoroutine(spawnRoutine);
			OnResumePausables();
			((IPausable)this).UnSubscribe();
		}

		public void OnPausePausables()
		{
			_isPaused = true;
		}

		public void OnResumePausables()
		{
			_isPaused = false;
		}

		private void OnDestroy()
		{
			((IPausable)this).UnSubscribe();
		}
	}
}
