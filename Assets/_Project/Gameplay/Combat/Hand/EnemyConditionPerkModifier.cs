using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public abstract class EnemyConditionPerkModifier : RuntimePerkModifier
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
			if (!(other is EnemyConditionPerkModifier enemyConditionPerkModifier))
			{
				return false;
			}
			parameters.multiplierIncrement += enemyConditionPerkModifier.parameters.multiplierIncrement;
			return true;
		}
	}
}
