using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public interface IDamageable
	{
		int GetID();

		Vector2 GetPosition();

		bool IsActive();

		void Damage(Vector2 attackPosition, WeaponBehaviour weapon, DamageType damageType);

		void Damage(int value, DamageType damageType);
	}
}
