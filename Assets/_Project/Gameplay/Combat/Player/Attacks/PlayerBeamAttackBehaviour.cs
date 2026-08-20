using System;
using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class PlayerBeamAttackBehaviour : WeaponBehaviour
	{
		[SerializeField]
		private AnimatedAttackVariants variants;

		public float spawnRadius = 1.5f;

		[SerializeField]
		protected float baseLerpSpeed = 4f;

		[SerializeField]
		private bool allowMultipleAttacks;

		private float _currentAngle;

		private float _previousAngle;

		private float _targetAngle;

		private readonly List<AnimatedAttack> _currentAttacks = new List<AnimatedAttack>();

		public override void Init(uint id, AttackStats stats)
		{
			base.Init(id, stats);
			_currentAttacks.Clear();
			variants.Init();
			LastAttackElapsedTime = GetCooldown() - Time.deltaTime;
		}

		private void Update()
		{
			if (_currentAttacks.Count > 0)
			{
				UpdateDirection();
				return;
			}
			if (CheckCooldown())
			{
				Attack();
			}
			LastAttackElapsedTime += Time.deltaTime;
		}

		private void UpdateDirection()
		{
			float num = Mathf.Atan2(player.attackDirection.y, player.attackDirection.x) * 57.29578f;
			float num2 = Mathf.DeltaAngle(_previousAngle, num);
			_targetAngle += num2;
			_previousAngle = num;
			_currentAngle = Mathf.Lerp(_currentAngle, _targetAngle, Time.deltaTime * baseLerpSpeed);
			if (_targetAngle > 360f)
			{
				_targetAngle -= 360f;
				_currentAngle -= 360f;
			}
			if (_targetAngle < -360f)
			{
				_targetAngle += 360f;
				_currentAngle += 360f;
			}
			int count = _currentAttacks.Count;
			for (int i = 0; i < count; i++)
			{
				float num3 = (float)i * (360f / (float)count);
				float num4 = _currentAngle + num3;
				Vector3 vector = new Vector3(Mathf.Cos(num4 * (MathF.PI / 180f)), Mathf.Sin(num4 * (MathF.PI / 180f)), 0f);
				_currentAttacks[i].transform.localPosition = vector * spawnRadius;
				_currentAttacks[i].UpdateRotation(vector);
			}
		}

		public override void Attack()
		{
			base.Attack();
			int num = ((!allowMultipleAttacks) ? 1 : base.ProjectileCountValue);
			for (int i = 0; i < num; i++)
			{
				AnimatedAttack orCreateAttack = GetOrCreateAttack();
				float num2 = Mathf.Atan2(player.attackDirection.y, player.attackDirection.x) * 57.29578f;
				float num3 = (float)i * (360f / (float)num);
				float num4 = num2 + num3;
				Vector3 vector = new Vector3(Mathf.Cos(num4 * (MathF.PI / 180f)), Mathf.Sin(num4 * (MathF.PI / 180f)), 0f);
				orCreateAttack.transform.localPosition = vector * spawnRadius;
				orCreateAttack.Attack(vector, base.DurationValue);
			}
		}

		private AnimatedAttack GetOrCreateAttack()
		{
			AnimatedAttack attack = variants.GetOrCreate(base.ActiveElement, base.transform, worldPositionStays: true);
			if (!_currentAttacks.Contains(attack))
			{
				_currentAttacks.Add(attack);
			}
			attack.Init(this, null, OnEnd);
			return attack;
			void OnEnd()
			{
				_currentAttacks.Remove(attack);
				variants.Return(attack);
				LastAttackElapsedTime = 0f;
			}
		}

		protected override void Dispose()
		{
			variants.Dispose();
			_currentAttacks.Clear();
		}
	}
}
