using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.Helpers.Attributes;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks.HomerAttacks
{
	public class ShieldAttackBehaviour : WeaponBehaviour
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

		private int _activeAttacks;

		[SerializeField]
		private int hp_denominator = 10;

		[SerializeField]
		private float scale_distance_mult = 3f;

		[SerializeField]
		private Transform positionOffsetTransform;

		[SerializeField]
		private float cooldownTime = 4f;

		[SerializeField]
		private LayerMask dashExclusionLayerMask;

		[SerializeField]
		private LayerMask defaultLayerMask;

		private List<Collider2D> _colliders = new List<Collider2D>();

		private Coroutine _movementCoroutine;

		public override void Init(uint id, AttackStats stats)
		{
			base.Init(id, stats);
			_pooler = PoolManager.Instance.GetOrCreatePooler(attackPrefab);
			_movementCoroutine = null;
			LastAttackElapsedTime = GetCooldown() - Time.deltaTime;
			player = GameDirector.Instance.Player;
			player.OnDashStart += SetDashCollision;
			player.OnDashEnd += SetDefaultCollision;
		}

		private void InitProjectiles()
		{
			_activeAttacks = _attacks.Count;
			for (int i = 0; i < _attacks.Count; i++)
			{
				if (_attacks != null)
				{
					float value = MathF.PI / 180f * (360f / (float)_attacks.Count) * (float)i;
					_angles[i] = value;
				}
				AnimatedAttack attack = _attacks[i];
				InitAttack(attack);
			}
		}

		private void InitAttack(AnimatedAttack attack)
		{
			Action onEnd = delegate
			{
				LastAttackElapsedTime = 0f;
				_activeAttacks--;
			};
			attack.Init(this, null, onEnd);
			attack.transform.localPosition = Vector3.zero;
			EnemyDamageableObject componentInChildren = attack.GetComponentInChildren<EnemyDamageableObject>();
			componentInChildren.MaxHealth = base.DurationValue * (float)player.PlayerStats.MaxHP / (float)hp_denominator;
			componentInChildren.ReviveObject();
			Collider2D component = componentInChildren.GetComponent<Collider2D>();
			component.excludeLayers = defaultLayerMask;
			if (!_colliders.Contains(component))
			{
				_colliders.Add(component);
			}
			componentInChildren.OnKilled = attack.PlayEndAnimation;
			componentInChildren.OnHit = attack.PlayHitAnimation;
			attack.Attack(Vector2.right, -1f);
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
			InitAttack(orCreateAttack);
		}

		private void RemoveProjectile(AnimatedAttack attack)
		{
			_attacks.Remove(attack);
			_angles.RemoveAt(_angles.Count - 1);
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
			UpdateProjectiles();
			InitProjectiles();
			if (_movementCoroutine != null)
			{
				StopCoroutine(_movementCoroutine);
			}
			_movementCoroutine = StartCoroutine(Movement());
		}

		public override float GetCooldown()
		{
			return cooldownTime / base.SpeedValue;
		}

		public override void UpdateModifiers(RuntimeEquipmentModifiers runtimeModifiers)
		{
			base.UpdateModifiers(runtimeModifiers);
			if (base.gameObject.activeInHierarchy)
			{
				Attack();
			}
		}

		private void UpdateProjectiles()
		{
			projectileCount = base.ProjectileCountValue;
			int num = projectileCount - _attacks.Count;
			if (num >= 0)
			{
				for (int i = 0; i < num; i++)
				{
					AddProjectile();
				}
				return;
			}
			for (int j = num; j < 0; j++)
			{
				if (_attacks.Count > 0)
				{
					RemoveProjectile(_attacks.Last());
				}
			}
		}

		private IEnumerator Movement()
		{
			float sizeValue = base.SizeValue;
			float speedValue = base.SpeedValue * baseSpeed;
			_elapsedAttackTime = 0f;
			while (_attacks.Count > 0 && _activeAttacks > 0)
			{
				for (int i = 0; i < _attacks.Count; i++)
				{
					_angles[i] += speedValue * Time.smoothDeltaTime;
					float x = Mathf.Cos(_angles[i]) * sizeValue * baseRadius;
					float y = Mathf.Sin(_angles[i]) * sizeValue * baseRadius;
					_attacks[i].UpdateRotation(new Vector2(x, y));
				}
				_elapsedAttackTime += Time.smoothDeltaTime;
				LastAttackElapsedTime = 0f;
				yield return null;
			}
			LastAttackElapsedTime = 0f;
			_movementCoroutine = null;
		}

		protected override void Dispose()
		{
			player.OnDashStart -= SetDashCollision;
			player.OnDashEnd -= SetDefaultCollision;
			if (_attacks != null)
			{
				for (int num = _attacks.Count - 1; num >= 0; num--)
				{
					RemoveProjectile(_attacks[num]);
				}
			}
			_attacks?.Clear();
			_angles?.Clear();
			_colliders?.Clear();
			LastAttackElapsedTime = 0f;
			_pooler = null;
			StopAllCoroutines();
			_movementCoroutine = null;
		}

		private void SetDashCollision()
		{
			if (_colliders != null)
			{
				for (int num = _colliders.Count - 1; num >= 0; num--)
				{
					_colliders[num].excludeLayers = dashExclusionLayerMask;
				}
			}
		}

		private void SetDefaultCollision()
		{
			if (_colliders != null)
			{
				for (int num = _colliders.Count - 1; num >= 0; num--)
				{
					_colliders[num].excludeLayers = defaultLayerMask;
				}
			}
		}
	}
}
