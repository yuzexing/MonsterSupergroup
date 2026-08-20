using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("Weapon Speed")]
	public class WeaponSpeedPerkModifier : WeaponStatsPerkModifier
	{
		public override void Apply(AttackStatsMultipliers multipliers)
		{
			multipliers.speed += parameters.multiplierIncrement;
		}
	}
}
