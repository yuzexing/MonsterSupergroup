using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("On Poison Afflicted Enemy Damage")]
	public class EnemyPoisonConditionPerkModifier : EnemyConditionPerkModifier
	{
		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.attackStatsMultipliers.poisonDamageMultiplier += parameters.multiplierIncrement;
		}
	}
}
