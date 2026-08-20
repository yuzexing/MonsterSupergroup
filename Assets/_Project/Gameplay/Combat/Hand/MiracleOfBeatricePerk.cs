using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("Receive Miracle Of Beatrice")]
	public class MiracleOfBeatricePerk : PlayerPerkModifier
	{
		[PerkModifierParams]
		protected new class ParamsData
		{
			public int reviveAmount;
		}

		[InjectPerkModifierParams]
		protected new ParamsData parameters;

		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.reviveChancesAmountReceiver = parameters.reviveAmount;
		}

		public override bool TryStack(RuntimePerkModifier other)
		{
			if (!(other is MiracleOfBeatricePerk miracleOfBeatricePerk))
			{
				return false;
			}
			parameters.reviveAmount = miracleOfBeatricePerk.parameters.reviveAmount;
			return true;
		}
	}
}
