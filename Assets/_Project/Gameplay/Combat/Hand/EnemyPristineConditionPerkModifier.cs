using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("Pristine Enemy Damage")]
	public class EnemyPristineConditionPerkModifier : EnemyConditionPerkModifier
	{
		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.attackStatsMultipliers.pristineDamageMultiplier += parameters.multiplierIncrement;
		}
	}
}
