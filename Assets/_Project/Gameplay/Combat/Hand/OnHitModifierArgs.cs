using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public struct OnHitModifierArgs
	{
		public BaseEnemyController Enemy;

		public WeaponBehaviour Weapon;

		public DamageInfo DamageInfo;

		public LegacyDamageSource Source;
	}
}
