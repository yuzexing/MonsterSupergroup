using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class ProjectileAttackBehaviour : WeaponBehaviour
	{
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
			base.Attack();
			PlayAttackSound();
			int projectileCountValue = base.ProjectileCountValue;
			if (projectileCountValue == 1)
			{
				ProjectileAttack orCreateAttack = GetOrCreateAttack();
				orCreateAttack.gameObject.SetActive(value: true);
				orCreateAttack.transform.position = base.transform.position + positionOffset + (Vector3)player.attackDirection.normalized * spawnRadius;
				orCreateAttack.Attack(player.attackDirection.normalized, baseSpeed, hitCount, rotateToMovement);
				LastAttackElapsedTime = 0f;
				return;
			}
			for (int i = 0; i < projectileCountValue; i++)
			{
				ProjectileAttack orCreateAttack2 = GetOrCreateAttack();
				orCreateAttack2.gameObject.SetActive(value: true);
				Vector3 vector = Quaternion.AngleAxis(Vector2.SignedAngle(player.attackDirection, Vector2.right) + 360f / (float)projectileCountValue * (float)i, -Vector3.forward) * Vector3.right;
				orCreateAttack2.transform.position = base.transform.position + positionOffset + vector.normalized * spawnRadius;
				orCreateAttack2.Attack(vector.normalized, baseSpeed, hitCount, rotateToMovement);
			}
			LastAttackElapsedTime = 0f;
		}

		protected ProjectileAttack GetOrCreateAttack()
		{
			ProjectileAttack attack = variants.GetOrCreate(base.ActiveElement, null);
			Action onEnd = delegate
			{
				variants.Return(attack);
			};
			attack.Init(this, null, onEnd);
			return attack;
		}

		protected override void Dispose()
		{
			variants.Dispose();
			LastAttackElapsedTime = 0f;
		}
	}
}
