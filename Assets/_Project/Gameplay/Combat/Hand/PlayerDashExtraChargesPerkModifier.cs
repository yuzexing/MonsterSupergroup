using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("Player Dash Extra Charges")]
	public class PlayerDashExtraChargesPerkModifier : PlayerPerkModifier
	{
		[PerkModifierParams]
		protected new class ParamsData
		{
			public int increment;
		}

		[InjectPerkModifierParams]
		protected new ParamsData parameters;

		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.extraDashCharges += parameters.increment;
			GameDirector.Instance.Player.PlayerStats.UpdateMaxDashes();
		}

		public override bool TryStack(RuntimePerkModifier other)
		{
			if (!(other is PlayerDashExtraChargesPerkModifier playerDashExtraChargesPerkModifier))
			{
				return false;
			}
			parameters.increment += playerDashExtraChargesPerkModifier.parameters.increment;
			return true;
		}
	}
}
