using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Common;
using AstralShift.HellMaiden.Helpers;
using AstralShift.HellMaiden.Items;
using AstralShift.Helpers.Attributes;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Spawners
{
	public abstract class EnemySpawner : SerializedProgressable
	{
		public int enemyCount = 30;

		public float distanceFromCamera;

		protected float _currentHP;

		public EnemyController enemyPrefab;

		protected GenericPooler<EnemyController> pool;

		public Action enemiesKilled;

		public int variantIdx;

		[SerializeField]
		[ReadOnly]
		protected int currentEnemyCount;

		[SerializeField]
		protected Vector2 EnemySpeedMultiplier = Vector2.one;

		protected const int GetSpawnPositionMaxIterations = 100;

		[SerializeField]
		protected LayerMask obstaclesLayerMask;

		[SerializeField]
		private bool rubberbandKillsEnemiesOnClipEnd;

		public Direction direction;

		public float angle = 20f;

		protected List<EnemyController> _enemies;

		public bool RubberbandKillsEnemiesOnClipEnd
		{
			get
			{
				return rubberbandKillsEnemiesOnClipEnd;
			}
			set
			{
				rubberbandKillsEnemiesOnClipEnd = value;
			}
		}

		public bool AllowRubberBand { get; set; } = true;

		protected void SetupPools(int poolsize)
		{
			pool = PoolManager.Instance.GetOrCreatePooler(enemyPrefab, poolsize);
			_enemies = new List<EnemyController>();
		}

		protected virtual void RegisterEnemyCount()
		{
			currentEnemyCount++;
			currentEnemyCount = Mathf.Clamp(currentEnemyCount, 0, int.MaxValue);
			ProgressionManager.Instance.RegisterEnemiesCount();
		}

		protected virtual void UnRegisterEnemyCount()
		{
			currentEnemyCount--;
			currentEnemyCount = Mathf.Clamp(currentEnemyCount, 0, int.MaxValue);
			ProgressionManager.Instance.UnRegisterEnemiesCount();
		}

		protected virtual bool GetSpawnPosition(out Vector2 spawnPosition)
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

		protected virtual EnemyController SpawnEnemy(Vector2 spawnPosition)
		{
			EnemyController enemyController = EnemyFactory.CreateEnemy(new EnemySpawnParams
			{
				Prefab = enemyPrefab,
				Pool = pool,
				AttackTarget = GameDirector.Instance.Player.EnemyAttackTarget,
				SpawnPosition = spawnPosition,
				VariantIdx = variantIdx,
				SpeedMultiplierRange = EnemySpeedMultiplier,
				AllowRubberBand = AllowRubberBand,
				RubberbandKillsEnemiesOnClipEnd = RubberbandKillsEnemiesOnClipEnd,
				EndTime = base.endTime,
				OnKill = UnRegisterEnemyCount
			});
			RegisterEnemyCount();
			_enemies.Add(enemyController);
			return enemyController;
		}

		public override void PauseProgressable()
		{
			base.PauseProgressable();
		}

		public override void ResumeProgressable()
		{
			base.ResumeProgressable();
		}

		public virtual void KillAllEnemies()
		{
			foreach (EnemyController enemy in _enemies)
			{
				enemy.overrideGlobalLootSettings = true;
				enemy.lootSettings = ScriptableObject.CreateInstance<LootSettingsData>();
				enemy.lootSettings.isXPMandatory = false;
				enemy.lootSettings.alwaysDrops = false;
				enemy.lootSettings.XPWeight = 0f;
				enemy.lootSettings.ItemsWeight = 0f;
				enemy.Kill();
			}
		}
	}
}
