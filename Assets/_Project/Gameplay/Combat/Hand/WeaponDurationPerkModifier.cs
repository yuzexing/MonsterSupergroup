using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("Weapon Duration")]
	public class WeaponDurationPerkModifier : WeaponStatsPerkModifier
	{
		public override void Apply(AttackStatsMultipliers multipliers)
		{
			multipliers.duration += parameters.multiplierIncrement;
		}
	}
}
