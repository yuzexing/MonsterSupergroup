using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("Knockback")]
	public class KnockBackStatModifier : StaticStatModifier
	{
		public override void Apply(AttackStatsMultipliers multipliers)
		{
			multipliers.knockBackMultiplier += parameters.multiplierIncrement;
		}
	}
}
