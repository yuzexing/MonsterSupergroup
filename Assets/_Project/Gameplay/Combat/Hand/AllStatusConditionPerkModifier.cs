using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("Status Afflicted Enemy Damage")]
	public class AllStatusConditionPerkModifier : EnemyConditionPerkModifier
	{
		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.attackStatsMultipliers.statusGeneralMultiplier += parameters.multiplierIncrement;
		}
	}
}
