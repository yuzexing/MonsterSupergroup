using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public abstract class PlayerConditionPerkModifier : RuntimePerkModifier
	{
		[PerkModifierParams]
		protected class ParamsData
		{
			public float multiplier;
		}

		[InjectPerkModifierParams]
		protected ParamsData parameters;

		public abstract void Apply(PlayerStats.PlayerStatsMultipliers multipliers);

		public override bool TryStack(RuntimePerkModifier other)
		{
			if (!(other is PlayerConditionPerkModifier playerConditionPerkModifier))
			{
				return false;
			}
			parameters.multiplier += playerConditionPerkModifier.parameters.multiplier;
			return true;
		}
	}
}
