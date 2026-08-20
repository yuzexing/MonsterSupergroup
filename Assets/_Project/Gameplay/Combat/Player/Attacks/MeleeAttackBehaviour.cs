using System;
using System.Collections;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat;
using AstralShift.Pooling;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class MeleeAttackBehaviour : WeaponBehaviour
	{
		[Header("Attack Settings")]
		public AnimatedAttack prefab;

		public float spawnRadius = 1f;

		public bool overrideAnimationLength;

		[ConditionalHide("overrideAnimationLength", true)]
		public float animationLength = 3f;

		public float multiProjectilesInterval = 0.1f;

		private GenericPooler<AnimatedAttack> _pooler;

		private Coroutine _attackCoroutine;

		private WaitForSeconds _projectilesLaunchIntervalYield;

		private List<AnimatedAttack> _attacks = new List<AnimatedAttack>();

		public override void Init(uint id, AttackStats stats)
		{
			base.Init(id, stats);
			LastAttackElapsedTime = GetCooldown() - Time.deltaTime;
			_attackCoroutine = null;
			_pooler = PoolManager.Instance.GetOrCreatePooler(prefab);
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
			if (_attackCoroutine == null)
			{
				_attackCoroutine = StartCoroutine(AttackCoroutine());
			}
		}

		private IEnumerator AttackCoroutine()
		{
			PlayAttackSound();
			int projectilesCount = base.ProjectileCountValue;
			Vector3 direction = player.attackDirection;
			if (_projectilesLaunchIntervalYield == null)
			{
				_projectilesLaunchIntervalYield = new WaitForSeconds(multiProjectilesInterval);
			}
			if (projectilesCount == 1)
			{
				SpawnAttack(0f, direction);
			}
			else
			{
				for (int i = 0; i < projectilesCount; i++)
				{
					SpawnAttack((float)(i - projectilesCount / 2) * 45f, direction);
					yield return _projectilesLaunchIntervalYield;
				}
			}
			_attackCoroutine = null;
			LastAttackElapsedTime = 0f;
		}

		private void SpawnAttack(float angle, Vector3 attackDirection)
		{
			AnimatedAttack orCreateAttack = GetOrCreateAttack();
			Vector3 vector = Quaternion.AngleAxis(Vector2.SignedAngle(attackDirection, Vector2.right) + angle, -Vector3.forward) * Vector3.right;
			orCreateAttack.transform.localPosition = vector.normalized * spawnRadius;
			if (overrideAnimationLength)
			{
				orCreateAttack.Attack(vector, animationLength);
			}
			else
			{
				orCreateAttack.Attack(vector);
			}
		}

		private AnimatedAttack GetOrCreateAttack()
		{
			AnimatedAttack attack = _pooler.GetOrCreate(base.transform, activate: true);
			if (!_attacks.Contains(attack))
			{
				_attacks.Add(attack);
			}
			Action onEnd = delegate
			{
				_attacks.Remove(attack);
				_pooler?.Return(attack);
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
			_pooler = null;
		}
	}
}
