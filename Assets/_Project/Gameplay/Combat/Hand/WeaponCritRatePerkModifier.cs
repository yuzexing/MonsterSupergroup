using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("Weapon Crit Rate")]
	public class WeaponCritRatePerkModifier : WeaponStatsPerkModifier
	{
		public override void Apply(AttackStatsMultipliers multipliers)
		{
			multipliers.critRate += parameters.multiplierIncrement;
		}
	}
}
