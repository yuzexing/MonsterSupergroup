using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Helpers;
using AstralShift.Helpers;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class AutoTargetAttackBehaviour : WeaponBehaviour
	{
		[Header("Attack Settings")]
		public AnimatedAttack attackPrefab;

		public bool overrideAnimationLengthWithDuration;

		public float multiProjectilesInterval = 0.2f;

		private GenericPooler<AnimatedAttack> _pooler;

		private Coroutine _attackCoroutine;

		private WaitForSeconds _projectilesLaunchIntervalYield;

		private List<AnimatedAttack> _attacks = new List<AnimatedAttack>();

		public override void Init(uint id, AttackStats stats)
		{
			base.Init(id, stats);
			_pooler = PoolManager.Instance.GetOrCreatePooler(attackPrefab);
			LastAttackElapsedTime = GetCooldown() - Time.deltaTime;
			_attackCoroutine = null;
		}

		private void Update()
		{
			if (CheckCooldown())
			{
				Attack();
			}
			LastAttackElapsedTime += Time.deltaTime;
		}

		public override void Attack()
		{
			base.Attack();
			if (AIHelpers.GetAllEnemiesOnScreen().Length != 0 && _attackCoroutine == null)
			{
				_attackCoroutine = StartCoroutine(AttackCoroutine());
			}
		}

		private IEnumerator AttackCoroutine()
		{
			if (_projectilesLaunchIntervalYield == null)
			{
				_projectilesLaunchIntervalYield = new WaitForSeconds(multiProjectilesInterval);
			}
			int projectileCount = base.ProjectileCountValue;
			List<BaseEnemyController> alreadyTargetedEnemies = new List<BaseEnemyController>();
			for (int i = 0; i < projectileCount; i++)
			{
				if (EnemyAIManager.Instance == null)
				{
					_attackCoroutine = null;
					yield break;
				}
				BaseEnemyController[] allEnemiesOnScreen = AIHelpers.GetAllEnemiesOnScreen();
				allEnemiesOnScreen = allEnemiesOnScreen.Where((BaseEnemyController enemy) => enemy.ID != -2).ToArray();
				List<BaseEnemyController> list = new List<BaseEnemyController>();
				list.AddRange(allEnemiesOnScreen);
				list.RemoveAll((BaseEnemyController enemy) => !ProCamera2DHelpers.IsWithinCameraBounds(enemy.transform.position));
				if (alreadyTargetedEnemies.Count > 0)
				{
					list.RemoveAll((BaseEnemyController enemy) => alreadyTargetedEnemies.Contains(enemy));
				}
				if (list.Count == 0)
				{
					if (alreadyTargetedEnemies.Count <= 0)
					{
						_attackCoroutine = null;
						if (i > 0)
						{
							LastAttackElapsedTime = 0f;
						}
						yield break;
					}
					int index = UnityEngine.Random.Range(0, alreadyTargetedEnemies.Count);
					Vector3 position = alreadyTargetedEnemies[index].transform.position;
					AnimatedAttack orCreateAttack = GetOrCreateAttack();
					orCreateAttack.transform.position = position;
					if (overrideAnimationLengthWithDuration)
					{
						orCreateAttack.Attack(Vector2.zero, base.DurationValue);
					}
					else
					{
						orCreateAttack.Attack(Vector2.zero);
					}
					yield return _projectilesLaunchIntervalYield;
				}
				else
				{
					int index = UnityEngine.Random.Range(0, list.Count);
					Vector3 position = list[index].transform.position;
					if (list[index].ID != -1)
					{
						alreadyTargetedEnemies.Add(list[index]);
					}
					AnimatedAttack orCreateAttack = GetOrCreateAttack();
					orCreateAttack.transform.position = position;
					if (overrideAnimationLengthWithDuration)
					{
						orCreateAttack.Attack(Vector2.zero, base.DurationValue);
					}
					else
					{
						orCreateAttack.Attack(Vector2.zero);
					}
					yield return _projectilesLaunchIntervalYield;
				}
			}
			LastAttackElapsedTime = 0f;
			_attackCoroutine = null;
		}

		private AnimatedAttack GetOrCreateAttack()
		{
			AnimatedAttack attack = _pooler.GetOrCreate(null, activate: true);
			if (!_attacks.Contains(attack))
			{
				_attacks.Add(attack);
			}
			Action onEnd = delegate
			{
				_attacks.Remove(attack);
				_pooler.Return(attack);
			};
			attack.Init(this, null, onEnd);
			return attack;
		}

		protected override void Dispose()
		{
			for (int num = _attacks.Count - 1; num >= 0; num--)
			{
				_pooler.Return(_attacks[num]);
			}
			_attacks.Clear();
			_attackCoroutine = null;
		}
	}
}
