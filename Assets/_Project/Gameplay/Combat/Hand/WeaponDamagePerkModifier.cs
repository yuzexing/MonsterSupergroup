using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("Weapon Damage")]
	public class WeaponDamagePerkModifier : WeaponStatsPerkModifier
	{
		public override void Apply(AttackStatsMultipliers multipliers)
		{
			multipliers.damage += parameters.multiplierIncrement;
		}
	}
}
