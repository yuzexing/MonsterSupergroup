using System.Collections;
using AstralShift.HellMaiden.Player.Attacks.ProjectileMovement;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks.VirgilAttacks
{
	public class VirgilProjectileAttackBehaviour : ProjectileAttackBehaviour
	{
		private Coroutine _attackCoroutine;

		public float multiProjectilesInterval = 0.1f;

		private WaitForSeconds _projectilesLaunchIntervalYield;

		private int projectileCounter;

		public override void Init(uint id, AttackStats stats)
		{
			base.Init(id, stats);
			_attackCoroutine = null;
			projectileCounter = 0;
		}

		public override void Attack()
		{
			EvaluateDynamicStatModifiers();
			if (_attackCoroutine == null)
			{
				_attackCoroutine = StartCoroutine(AttackCoroutine());
			}
		}

		private IEnumerator AttackCoroutine()
		{
			PlayAttackSound();
			int projectileCountValue = base.ProjectileCountValue;
			if (_projectilesLaunchIntervalYield == null)
			{
				_projectilesLaunchIntervalYield = new WaitForSeconds(multiProjectilesInterval);
			}
			bool startMaxAmp = true;
			for (projectileCounter = projectileCountValue; projectileCounter > 0; projectileCounter -= 2)
			{
				if (projectileCounter == 1)
				{
					SpawnAttack(1, startMaxAmp);
				}
				else
				{
					SpawnAttack(2, startMaxAmp);
				}
				startMaxAmp = !startMaxAmp;
				yield return _projectilesLaunchIntervalYield;
			}
			_attackCoroutine = null;
			LastAttackElapsedTime = 0f;
		}

		private void SpawnAttack(int nProjectiles, bool maxAmp)
		{
			bool flag = false;
			for (int i = 0; i < nProjectiles; i++)
			{
				ProjectileAttack orCreateAttack = GetOrCreateAttack();
				PM_SinCosWave component = orCreateAttack.GetComponent<PM_SinCosWave>();
				component.startMaxAmp = maxAmp;
				flag = ((i != 0) ? (component.isSin = !flag) : component.isSin);
				orCreateAttack.gameObject.SetActive(value: true);
				orCreateAttack.transform.position = base.transform.position + positionOffset + (Vector3)player.attackDirection.normalized * spawnRadius;
				orCreateAttack.Attack(player.attackDirection.normalized, baseSpeed, hitCount, rotateToMovement);
			}
		}
	}
}
