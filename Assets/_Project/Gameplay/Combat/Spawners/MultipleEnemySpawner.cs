using System.Collections;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat.Spawners.SpawnShapes;
using AstralShift.Helpers;
using AstralShift.Pooling;
using AstralShift.QTI.Helpers.Attributes;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Spawners
{
	public class MultipleEnemySpawner : EnemySpawner
	{
		public enum SpawnShapeOptions
		{
			Random = -1,
			Circle = 0,
			Triangle = 1,
			Rectangle = 2
		}

		public bool isTrap = true;

		public float waitForEffectsStart = 1f;

		public float waitForEnemyActivation = 1f;

		[Tooltip("Just OnDrawGizmos")]
		public SpawnShape spawnShape;

		public List<SpawnShape> spawnShapes;

		[SerializeField]
		private bool centerIsPlayer;

		[ConditionalHide("centerIsPlayer", false)]
		public Transform Center;

		public ParticleSystem particleSystem;

		private GenericPooler<ParticleSystem> _particleSystemPooler;

		public SpawnShapeOptions spawnShapeOptions;

		public bool useRandomEnemies;

		public EnemyController[] randomEnemies;

		private List<GenericPooler<EnemyController>> randomPools;

		[SerializeField]
		private int enemySpawnInterval = 3;

		[SerializeField]
		private bool spawnSound;

		[SerializeField]
		private EventReference spawnSoundEvent;

		public Vector2 _center => Center.position;

		public override void Init()
		{
			if (isTrap && ProgressionManager.Instance.ReachedMaxTrapCount)
			{
				return;
			}
			if (!useRandomEnemies)
			{
				SetupPools(enemyCount);
			}
			else
			{
				SetupMultiplePools();
			}
			if (spawnShapes != null && spawnShapes.Count > 0)
			{
				if (spawnShapeOptions == SpawnShapeOptions.Random)
				{
					List<SpawnShape> list = new List<SpawnShape>();
					for (int i = 0; i < spawnShapes.Count; i++)
					{
						if (spawnShapes[i].ValidVertexCount(enemyCount))
						{
							list.Add(spawnShapes[i]);
						}
					}
					spawnShape = list[Random.Range(0, list.Count)];
				}
				else
				{
					spawnShape = spawnShapes[(int)spawnShapeOptions];
					if (!spawnShape.ValidVertexCount(enemyCount))
					{
						Debug.LogError("Enemy count is incompatible with spawn shape!");
						return;
					}
				}
			}
			if (_particleSystemPooler == null)
			{
				_particleSystemPooler = PoolManager.Instance.GetOrCreatePooler(particleSystem);
			}
			if (isTrap)
			{
				ProgressionManager.Instance.RegisterTrapCount(null);
				StartCoroutine(Wait.SetTimeout(base.Duration, delegate
				{
					ProgressionManager.Instance.UnRegisterTrapCount(null);
				}));
			}
			StartCoroutine(SpawnEnemiesCoroutine());
			if (spawnSound)
			{
				RuntimeManager.PlayOneShot(spawnSoundEvent);
			}
		}

		private IEnumerator SpawnEnemiesCoroutine()
		{
			yield return new WaitForSeconds(waitForEffectsStart);
			if (centerIsPlayer)
			{
				Center = GameDirector.Instance.Player.transform;
			}
			List<ParticleSystem> particleSystems = new List<ParticleSystem>();
			List<Vector2> positions = new List<Vector2>();
			for (int i = 0; i < enemyCount; i++)
			{
				Vector2 enemyPosition = spawnShape.GetEnemyPosition(_center, enemyCount, i);
				if (_particleSystemPooler != null)
				{
					ParticleSystem orCreate = _particleSystemPooler.GetOrCreate(base.transform);
					orCreate.transform.position = enemyPosition;
					orCreate.gameObject.SetActive(value: true);
					orCreate.Play();
					particleSystems.Add(orCreate);
				}
				positions.Add(enemyPosition);
			}
			yield return new WaitForSeconds(waitForEnemyActivation);
			for (int j = 0; j < positions.Count; j++)
			{
				if (j % enemySpawnInterval == 0)
				{
					SpawnEnemy(positions[j]);
				}
				if (_particleSystemPooler != null)
				{
					particleSystems[j].Stop(withChildren: true);
				}
			}
			base.hasEnded = true;
			for (int i2 = particleSystems.Count - 1; i2 >= 0; i2--)
			{
				while (particleSystems[i2].IsAlive(withChildren: true))
				{
					yield return null;
				}
				_particleSystemPooler.Return(particleSystems[i2]);
				particleSystems.Remove(particleSystems[i2]);
			}
		}

		protected override void UnRegisterEnemyCount()
		{
			currentEnemyCount--;
			if (currentEnemyCount <= 0)
			{
				enemiesKilled?.Invoke();
			}
		}

		protected override void RegisterEnemyCount()
		{
			currentEnemyCount++;
		}

		private void SetupMultiplePools()
		{
			randomPools = new List<GenericPooler<EnemyController>>();
			for (int i = 0; i < randomEnemies.Length; i++)
			{
				randomPools.Add(PoolManager.Instance.GetOrCreatePooler(randomEnemies[i], enemyCount));
			}
			_enemies = new List<EnemyController>();
		}

		protected override EnemyController SpawnEnemy(Vector2 spawnPosition)
		{
			if (useRandomEnemies)
			{
				pool = randomPools[Random.Range(0, randomPools.Count)];
			}
			EnemyController enemyController = EnemyFactory.CreateEnemy(new EnemySpawnParams
			{
				Prefab = enemyPrefab,
				Pool = pool,
				AttackTarget = GameDirector.Instance.Player.EnemyAttackTarget,
				SpawnPosition = spawnPosition,
				VariantIdx = variantIdx,
				SpeedMultiplierRange = EnemySpeedMultiplier,
				AllowRubberBand = base.AllowRubberBand,
				RubberbandKillsEnemiesOnClipEnd = base.RubberbandKillsEnemiesOnClipEnd,
				EndTime = base.endTime,
				OnConfirmedKill = UnRegisterEnemyCount
			});
			RegisterEnemyCount();
			_enemies.Add(enemyController);
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
