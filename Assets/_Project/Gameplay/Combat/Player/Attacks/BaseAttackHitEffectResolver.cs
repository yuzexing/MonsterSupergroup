using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public abstract class BaseAttackHitEffectResolver : MonoBehaviour
	{
		[SerializeField]
		protected DamageMode damageMode;

		public DamageMode DamageMode => damageMode;

		public abstract void Initialize(WeaponBehaviour behaviour = null);
	}
}
