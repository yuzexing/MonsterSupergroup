using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("Crit Damage")]
	public class CritMultiplierStatModifier : StaticStatModifier
	{
		public override void Apply(AttackStatsMultipliers multipliers)
		{
			multipliers.critDamage += parameters.multiplierIncrement;
		}
	}
}
