using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.QTI.Triggers;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class OnDamageTrigger : InteractionTrigger, IDamageable
	{
		public int GetID()
		{
			return GetEntityId();
		}

		public Vector2 GetPosition()
		{
			return base.transform.position;
		}

		public bool IsActive()
		{
			if (base.gameObject.activeSelf)
			{
				return base.enabled;
			}
			return false;
		}

		public void Damage(Vector2 attackPosition, WeaponBehaviour weapon, DamageType damageType)
		{
			base.Interact(GameDirector.Instance.Player.interactionFinder);
		}

		public void Damage(int value, DamageType damageType)
		{
			base.Interact(GameDirector.Instance.Player.interactionFinder);
		}
	}
}
