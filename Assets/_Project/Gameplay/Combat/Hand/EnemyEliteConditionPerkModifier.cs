using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("On Elite Enemy Damage")]
	public class EnemyEliteConditionPerkModifier : EnemyConditionPerkModifier
	{
		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.attackStatsMultipliers.eliteDamageMultiplier += parameters.multiplierIncrement;
		}
	}
}
