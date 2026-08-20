using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Common;
using AstralShift.HellMaiden.Helpers;
using AstralShift.HellMaiden.Items;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Spawners
{
	public class ContinuousWaveDefenceSpawner : ContinuousLimitedEnemySpawner
	{
		public AnimationCurve spawnCurve;

		public Transform target;

		private List<EnemyController> _spawnedEnemies = new List<EnemyController>();

		public GameObject hudIndicator;

		protected Coroutine _spawnRoutine;

		private List<float> spawnTimestamps;

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
			GameObject arrow = null;
			EnemyController enemyController = EnemyFactory.CreateEnemy(new EnemySpawnParams
			{
				Prefab = enemyPrefab,
				Pool = pool,
				AttackTarget = target,
				VariantIdx = variantIdx,
				SpawnPosition = spawnPosition,
				SpeedMultiplierRange = EnemySpeedMultiplier,
				AllowRubberBand = base.AllowRubberBand,
				RubberbandKillsEnemiesOnClipEnd = base.RubberbandKillsEnemiesOnClipEnd,
				EndTime = base.endTime
			});
			enemyController.OnDispose += OnDisposeCleanup;
			if ((bool)hudIndicator)
			{
				arrow = Object.Instantiate(hudIndicator, enemyController.OnHitEffectTopPivot);
			}
			if (direction != Direction.None)
			{
				enemyController.direction = direction;
				enemyController.angle = angle;
			}
			RegisterEnemyCount();
			if (!_spawnedEnemies.Contains(enemyController))
			{
				_spawnedEnemies.Add(enemyController);
			}
			return enemyController;
			void OnDisposeCleanup()
			{
				UnRegisterEnemyCount();
				if (arrow != null)
				{
					Object.Destroy(arrow);
				}
			}
		}

		public override void Init()
		{
			SetupPools(enemyCount);
			base.startTime = 0f;
			spawnIntervalYield = new WaitForSeconds(spawnCooldown);
			_spawnRoutine = StartCoroutine(SpawnRoutine());
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

		public override void KillAllEnemies()
		{
			foreach (EnemyController spawnedEnemy in _spawnedEnemies)
			{
				spawnedEnemy.overrideGlobalLootSettings = true;
				spawnedEnemy.lootSettings = ScriptableObject.CreateInstance<LootSettingsData>();
				spawnedEnemy.lootSettings.isXPMandatory = false;
				spawnedEnemy.lootSettings.alwaysDrops = false;
				spawnedEnemy.lootSettings.XPWeight = 0f;
				spawnedEnemy.lootSettings.ItemsWeight = 0f;
				spawnedEnemy.Kill();
			}
			_spawnedEnemies.Clear();
		}

		public override void End()
		{
			if (spawnRoutine != null)
			{
				StopCoroutine(spawnRoutine);
			}
		}
	}
}
