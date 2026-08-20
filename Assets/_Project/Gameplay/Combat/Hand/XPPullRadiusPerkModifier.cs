using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("XP Pull Radius")]
	public class XPPullRadiusPerkModifier : PlayerPerkModifier
	{
		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.xpPullRadiusMultiplier += parameters.multiplierIncrement;
		}
	}
}
