using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("Player Move Speed")]
	public class PlayerMoveSpeedPerkModifier : PlayerPerkModifier
	{
		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.moveSpeedMultiplier += parameters.multiplierIncrement;
		}
	}
}
