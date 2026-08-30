using System;
using MonsterSupergroup.GAS;
using UnityEngine;
using CombatTags = MonsterSupergroup.GAS.CombatTags;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class ProjectileAttackBehaviour : WeaponBehaviour
	{
		protected override CombatTags DefaultCombatTags =>
			CombatTags.Attack | CombatTags.Projectile;

		[Header("Attack Settings")]
		[SerializeField]
		protected ProjectileAttackVariants variants;

		[SerializeField]
		protected float baseSpeed = 3f;

		[SerializeField]
		protected float spawnRadius = 0.5f;

		[SerializeField]
		protected int hitCount = 1;

		[SerializeField]
		protected bool rotateToMovement = true;

		[SerializeField]
		protected Vector3 positionOffset = Vector3.zero;

		public override void Init(uint id, AttackStats stats)
		{
			base.Init(id, stats);
			variants.Init();
			LastAttackElapsedTime = GetCooldown() - Time.deltaTime;
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
			AttackSnapshot nativeAttack = null;
			if (UsesNativeGasRuntime)
			{
				nativeAttack = BeginNativeGasAttack();
			}
			else
			{
				base.Attack();
			}

			try
			{
				PlayAttackSound();
				int projectileCountValue = nativeAttack != null
					? nativeAttack.Stats.ProjectileCount
					: base.ProjectileCountValue;
				if (projectileCountValue == 1)
				{
					ProjectileAttack orCreateAttack = GetOrCreateAttack(nativeAttack);
					orCreateAttack.gameObject.SetActive(value: true);
					orCreateAttack.transform.position = base.transform.position + positionOffset + (Vector3)player.attackDirection.normalized * spawnRadius;
					orCreateAttack.Attack(player.attackDirection.normalized, baseSpeed, hitCount, rotateToMovement);
					LastAttackElapsedTime = 0f;
					return;
				}
				for (int i = 0; i < projectileCountValue; i++)
				{
					ProjectileAttack orCreateAttack2 = GetOrCreateAttack(nativeAttack);
					orCreateAttack2.gameObject.SetActive(value: true);
					Vector3 vector = Quaternion.AngleAxis(Vector2.SignedAngle(player.attackDirection, Vector2.right) + 360f / (float)projectileCountValue * (float)i, -Vector3.forward) * Vector3.right;
					orCreateAttack2.transform.position = base.transform.position + positionOffset + vector.normalized * spawnRadius;
					orCreateAttack2.Attack(vector.normalized, baseSpeed, hitCount, rotateToMovement);
				}
				LastAttackElapsedTime = 0f;
			}
			finally
			{
				nativeAttack?.Dispose();
			}
		}

		protected ProjectileAttack GetOrCreateAttack(AttackSnapshot nativeAttack = null)
		{
			ProjectileAttack attack = variants.GetOrCreate(base.ActiveElement, null);
			Action onEnd = delegate
			{
				attack.ReleaseNativeAttackSnapshot();
				variants.Return(attack);
			};
			if (nativeAttack != null)
			{
				attack.InitNative(this, nativeAttack, null, onEnd);
			}
			else
			{
				attack.Init(this, null, onEnd);
			}
			return attack;
		}

		protected override void Dispose()
		{
			variants.Dispose(attack => attack.ReleaseNativeAttackSnapshot());
			LastAttackElapsedTime = 0f;
		}
	}
}
