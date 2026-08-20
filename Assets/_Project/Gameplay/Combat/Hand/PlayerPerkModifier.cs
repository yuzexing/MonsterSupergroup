using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public abstract class PlayerPerkModifier : RuntimePerkModifier
	{
		[PerkModifierParams]
		protected class ParamsData
		{
			public float multiplierIncrement;
		}

		[InjectPerkModifierParams]
		protected ParamsData parameters;

		public abstract void Apply(PlayerStats.PlayerStatsMultipliers multipliers);

		public override bool TryStack(RuntimePerkModifier other)
		{
			if (!(other is PlayerPerkModifier playerPerkModifier))
			{
				return false;
			}
			parameters.multiplierIncrement += playerPerkModifier.parameters.multiplierIncrement;
			return true;
		}
	}
}
