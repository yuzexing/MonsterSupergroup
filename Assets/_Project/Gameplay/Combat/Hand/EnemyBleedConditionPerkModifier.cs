using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("On Bleed Afflicted Enemy Damage")]
	public class EnemyBleedConditionPerkModifier : EnemyConditionPerkModifier
	{
		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.attackStatsMultipliers.bleedDamageMultiplier += parameters.multiplierIncrement;
		}
	}
}
