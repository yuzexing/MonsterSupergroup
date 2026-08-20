using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("XP Amount")]
	public class XPAmountPerkModifier : PlayerPerkModifier
	{
		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.xpAmountMultiplier += parameters.multiplierIncrement;
		}
	}
}
