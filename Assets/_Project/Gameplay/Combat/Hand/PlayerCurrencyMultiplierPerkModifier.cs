using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("Currency Multiplier")]
	public class PlayerCurrencyMultiplierPerkModifier : PlayerPerkModifier
	{
		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.currencyMultiplier = parameters.multiplierIncrement;
		}
	}
}
