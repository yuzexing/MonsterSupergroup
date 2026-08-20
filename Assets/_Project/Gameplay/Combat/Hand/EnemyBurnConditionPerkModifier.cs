using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("On Burn Afflicted Enemy Damage")]
	public class EnemyBurnConditionPerkModifier : EnemyConditionPerkModifier
	{
		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.attackStatsMultipliers.burnDamageMultiplier += parameters.multiplierIncrement;
		}
	}
}
