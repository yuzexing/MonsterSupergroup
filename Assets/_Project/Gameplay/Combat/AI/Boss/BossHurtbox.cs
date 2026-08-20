using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss
{
	public class BossHurtbox : MonoBehaviour, IDamageable
	{
		[SerializeField]
		public BossController controller;

		public int GetID()
		{
			return GetEntityId();
		}

		public Vector2 GetPosition()
		{
			return controller.Transform.position;
		}

		public bool IsActive()
		{
			return base.gameObject.activeSelf;
		}

		public void Damage(Vector2 attackPosition, WeaponBehaviour weapon, DamageType damageType)
		{
		}

		public void Damage(int value, DamageType damageType)
		{
		}
	}
}
