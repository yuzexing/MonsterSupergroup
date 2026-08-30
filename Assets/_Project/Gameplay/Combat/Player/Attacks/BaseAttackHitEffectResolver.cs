using UnityEngine;
using MonsterSupergroup.GAS;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public abstract class BaseAttackHitEffectResolver : MonoBehaviour
	{
		[SerializeField]
		protected DamageMode damageMode;

		public DamageMode DamageMode => damageMode;

		public abstract void Initialize(WeaponBehaviour behaviour = null);

		public virtual void Initialize(
			WeaponBehaviour behaviour,
			AttackSnapshot attack)
		{
			Initialize(behaviour);
		}
	}
}
