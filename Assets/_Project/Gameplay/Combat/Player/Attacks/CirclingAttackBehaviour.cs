using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Combat;
using AstralShift.Helpers.Attributes;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class CirclingAttackBehaviour : WeaponBehaviour
	{
		[Header("Attack Settings")]
		public AnimatedAttack attackPrefab;

		public float baseRadius = 1.5f;

		public float baseSpeed = 2f;

		public float baseCooldown = 5f;

		private float _elapsedAttackTime;

		private int projectileCount;

		private int minimumProjectileCount = 1;

		[SerializeField]
		[ReadOnly]
		private List<AnimatedAttack> _attacks;

		private GenericPooler<AnimatedAttack> _pooler;

		private List<float> _angles;

		private Coroutine _movementCoroutine;

		public override void Init(uint id, AttackStats stats)
		{
			base.Init(id, stats);
			_pooler = PoolManager.Instance.GetOrCreatePooler(attackPrefab);
			_movementCoroutine = null;
			LastAttackElapsedTime = GetCooldown() - Time.deltaTime;
		}

		private void InitProjectiles()
		{
			for (int i = 0; i < _attacks.Count; i++)
			{
				if (_attacks != null)
				{
					float value = MathF.PI / 180f * (360f / (float)_attacks.Count) * (float)i;
					_angles[i] = value;
				}
				AnimatedAttack attack = _attacks[i];
				Action onEnd = delegate
				{
					RemoveProjectile(attack);
				};
				_attacks[i].Init(this, null, onEnd);
				_attacks[i].PlayStartAnimation();
			}
		}

		private void Update()
		{
			if (CheckCooldown())
			{
				LastAttackElapsedTime = 0f;
				Attack();
			}
			else
			{
				LastAttackElapsedTime += Time.deltaTime;
			}
		}

		private void AddProjectile()
		{
			if (_angles == null)
			{
				_angles = new List<float>();
			}
			if (_attacks == null)
			{
				_attacks = new List<AnimatedAttack>();
			}
			_angles.Add(0f);
			AnimatedAttack orCreateAttack = GetOrCreateAttack();
			_attacks.Add(orCreateAttack);
		}

		private void RemoveProjectile(AnimatedAttack attack)
		{
			_attacks.Remove(attack);
			if (_pooler != null)
			{
				_pooler.Return(attack);
			}
			else
			{
				UnityEngine.Object.Destroy(attack);
			}
		}

		protected AnimatedAttack GetOrCreateAttack()
		{
			return _pooler.GetOrCreate(base.transform, activate: true);
		}

		public override void Attack()
		{
			base.Attack();
			projectileCount = base.ProjectileCountValue;
			int num = projectileCount - _attacks.Count;
			if (num >= 0)
			{
				for (int i = 0; i < num; i++)
				{
					AddProjectile();
				}
			}
			else
			{
				for (int j = num; j < 0; j++)
				{
					if (_attacks.Count > 0)
					{
						RemoveProjectile(_attacks.Last());
					}
				}
			}
			InitProjectiles();
			if (_movementCoroutine != null)
			{
				StopCoroutine(_movementCoroutine);
			}
			_movementCoroutine = StartCoroutine(Movement());
		}

		private IEnumerator Movement()
		{
			float sizeValue = base.SizeValue;
			float speedValue = base.SpeedValue * baseSpeed;
			float durationValue = base.DurationValue;
			_elapsedAttackTime = 0f;
			while (_elapsedAttackTime < durationValue)
			{
				for (int i = 0; i < _attacks.Count; i++)
				{
					_angles[i] += speedValue * Time.smoothDeltaTime;
					float x = Mathf.Cos(_angles[i]) * sizeValue * baseRadius;
					float y = Mathf.Sin(_angles[i]) * sizeValue * baseRadius;
					_attacks[i].transform.localPosition = new Vector3(x, y, 0f);
				}
				_elapsedAttackTime += Time.smoothDeltaTime;
				LastAttackElapsedTime = 0f;
				yield return null;
			}
			for (int j = 0; j < _attacks.Count; j++)
			{
				_attacks[j].PlayEndAnimation();
			}
			LastAttackElapsedTime = 0f;
			_movementCoroutine = null;
		}

		protected override void Dispose()
		{
			if (_attacks != null)
			{
				for (int num = _attacks.Count - 1; num >= 0; num--)
				{
					_pooler?.Return(_attacks[num]);
				}
			}
			_attacks?.Clear();
			_angles?.Clear();
			LastAttackElapsedTime = 0f;
			_pooler = null;
		}
	}
}
