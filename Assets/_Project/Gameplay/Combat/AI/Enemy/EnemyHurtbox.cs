using System;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	[RequireComponent(typeof(Collider2D))]
	public class EnemyHurtbox : MonoBehaviour, IDamageable
	{
		[SerializeField]
		protected Collider2D collider;

		public event Action<Vector2, WeaponBehaviour, DamageType> OnDamageWeapon;

		public event Action<int, DamageType> OnDamageGeneric;

		public void Reset()
		{
			collider = GetComponent<Collider2D>();
		}

		public int GetID()
		{
			return GetEntityId();
		}

		public Vector2 GetPosition()
		{
			if (!collider)
			{
				return base.transform.position;
			}
			return (Vector2)collider.transform.position + collider.offset;
		}

		public bool IsActive()
		{
			if (collider.enabled)
			{
				return base.gameObject.activeSelf;
			}
			return false;
		}

		public void Damage(Vector2 attackPosition, WeaponBehaviour weapon, DamageType damageType)
		{
			this.OnDamageWeapon?.Invoke(attackPosition, weapon, damageType);
		}

		public void Damage(int value, DamageType damageType)
		{
			this.OnDamageGeneric?.Invoke(value, damageType);
		}

		public virtual void ActivateCollider(bool state)
		{
			collider.enabled = state;
		}

		public Bounds GetBounds()
		{
			return collider.bounds;
		}
	}
}
