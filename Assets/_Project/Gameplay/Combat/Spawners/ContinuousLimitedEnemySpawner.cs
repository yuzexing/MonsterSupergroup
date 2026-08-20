using System.Collections;
using AstralShift.Managers;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Spawners
{
	public class ContinuousLimitedEnemySpawner : EnemySpawner, IPausable
	{
		public float spawnCooldown;

		private float _startTime;

		protected Coroutine spawnRoutine;

		protected WaitForSeconds spawnIntervalYield;

		private bool _isPaused;

		public bool onlyFinishOnDeath;

		private Coroutine verifyDeath;

		public override void Init()
		{
			SetupPools(enemyCount);
			((IPausable)this).Subscribe();
			spawnIntervalYield = new WaitForSeconds(spawnCooldown);
			currentEnemyCount = 0;
			spawnRoutine = StartCoroutine(SpawnRoutine());
		}

		public virtual IEnumerator SpawnRoutine()
		{
			while (ProgressionManager.Instance.CurrentTime <= base.endTime)
			{
				if (_isPaused)
				{
					yield return null;
				}
				if (currentEnemyCount < enemyCount && !ProgressionManager.Instance.ReachedMaxEnemiesCount)
				{
					for (int i = currentEnemyCount; i < enemyCount; i++)
					{
						yield return new WaitWhile(() => base.progressionPaused);
						if (GetSpawnPosition(out var spawnPosition))
						{
							SpawnEnemy(spawnPosition);
						}
						yield return spawnIntervalYield;
					}
				}
				if (verifyDeath == null)
				{
					verifyDeath = StartCoroutine(VerifyDeath());
				}
				yield return null;
			}
			if (!onlyFinishOnDeath)
			{
				base.hasEnded = true;
			}
		}

		private IEnumerator VerifyDeath()
		{
			while (currentEnemyCount > 0)
			{
				yield return null;
			}
			enemiesKilled?.Invoke();
			if (onlyFinishOnDeath)
			{
				base.hasEnded = true;
			}
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

		public override void ProgressUpdate()
		{
		}

		private void OnDestroy()
		{
			((IPausable)this).UnSubscribe();
		}
	}
}
