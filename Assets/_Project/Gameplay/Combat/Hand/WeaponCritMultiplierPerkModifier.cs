using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("Weapon Crit Damage")]
	public class WeaponCritMultiplierPerkModifier : WeaponStatsPerkModifier
	{
		public override void Apply(AttackStatsMultipliers multipliers)
		{
			multipliers.critDamage += parameters.multiplierIncrement;
		}
	}
}
