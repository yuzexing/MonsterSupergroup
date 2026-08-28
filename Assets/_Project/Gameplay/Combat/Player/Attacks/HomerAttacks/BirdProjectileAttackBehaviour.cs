using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.Pooling;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;
using CombatTags = MonsterSupergroup.GAS.CombatTags;

namespace AstralShift.HellMaiden.Player.Attacks.HomerAttacks
{
	public class BirdProjectileAttackBehaviour : WeaponBehaviour
	{
		protected override CombatTags DefaultCombatTags =>
			CombatTags.Attack | CombatTags.Projectile;

		private GenericPooler<ProjectileLauncherAttack> _pooler;

		private Coroutine _attackCoroutine;

		private ProjectileLauncherAttack _attack;

		public bool overrideAnimationLength;

		[ConditionalHide("overrideAnimationLength", true)]
		public float animationLength = 3f;

		private List<ProjectileLauncherAttack> _attacks = new List<ProjectileLauncherAttack>();

		[SerializeField]
		private float spawnRadius = 0.5f;

		[SerializeField]
		private ProjectileLauncherAttack _attackPrefab;

		private void Start()
		{
			Vector3 attackDirection = player.attackDirection;
			_attack = SpawnAttack(0f, attackDirection);
			_attackCoroutine = null;
			LastAttackElapsedTime = 0f;
		}

		public override void UpdateModifiers(RuntimeEquipmentModifiers runtimeModifiers)
		{
			base.UpdateModifiers(runtimeModifiers);
			if (_attack != null)
			{
				Action onEnd = delegate
				{
					_attack.animancer.Stop();
				};
				_attack.Init(this, null, onEnd);
			}
		}

		protected override void Dispose()
		{
		}

		protected void Update()
		{
			if (CheckCooldown())
			{
				Attack();
			}
			LastAttackElapsedTime += Time.deltaTime;
		}

		public override void Attack()
		{
			EvaluateDynamicStatModifiers();
			PlayAttackSound();
			_attack.Attack();
			LastAttackElapsedTime = 0f;
		}

		private ProjectileLauncherAttack SpawnAttack(float angle, Vector3 attackDirection)
		{
			return GetOrCreateAttack();
		}

		private ProjectileLauncherAttack GetOrCreateAttack()
		{
			if (_pooler == null)
			{
				_pooler = PoolManager.Instance.GetOrCreatePooler(_attackPrefab);
			}
			ProjectileLauncherAttack attack = _pooler.GetOrCreate(base.transform, activate: true);
			if (!_attacks.Contains(attack))
			{
				_attacks.Add(attack);
			}
			Action onEnd = delegate
			{
				attack.animancer.Stop();
			};
			attack.Init(this, null, onEnd);
			return attack;
		}
	}
}
