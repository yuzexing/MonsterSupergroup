using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("Size")]
	public class SizeStatModifier : StaticStatModifier
	{
		public override void Apply(AttackStatsMultipliers multipliers)
		{
			multipliers.size += parameters.multiplierIncrement;
		}
	}
}
