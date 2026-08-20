using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("Player Pristine Condition Damage")]
	public class PlayerPristineConditionDamagePerkModifier : PlayerConditionPerkModifier
	{
		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.attackStatsMultipliers.playerFullHealthMultiplier += parameters.multiplier;
		}
	}
}
