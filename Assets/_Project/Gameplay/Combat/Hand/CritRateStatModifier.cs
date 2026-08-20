using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("Crit Rate")]
	public class CritRateStatModifier : StaticStatModifier
	{
		public override void Apply(AttackStatsMultipliers multipliers)
		{
			multipliers.critRate += parameters.multiplierIncrement;
		}
	}
}
