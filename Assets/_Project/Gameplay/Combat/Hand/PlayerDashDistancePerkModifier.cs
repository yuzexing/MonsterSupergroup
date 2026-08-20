using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("Player Dash Distance")]
	public class PlayerDashDistancePerkModifier : PlayerPerkModifier
	{
		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.dashDistanceMultiplier += parameters.multiplierIncrement;
		}
	}
}
