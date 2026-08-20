using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("Extra Weapon Knockback")]
	public class WeaponKnockBackPerkModifier : WeaponStatsPerkModifier
	{
		public override void Apply(AttackStatsMultipliers multipliers)
		{
			multipliers.knockBackMultiplier += parameters.multiplierIncrement;
		}
	}
}
