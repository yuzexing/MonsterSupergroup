using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("Damage")]
	public class DamageStatModifier : StaticStatModifier
	{
		public override void Apply(AttackStatsMultipliers multipliers)
		{
			multipliers.damage += parameters.multiplierIncrement;
		}
	}
}
