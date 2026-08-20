using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public abstract class WeaponStatsPerkModifier : RuntimePerkModifier
	{
		[PerkModifierParams]
		protected class BaseParamsData
		{
			public float multiplierIncrement;
		}

		[InjectPerkModifierParams]
		protected BaseParamsData parameters;

		public abstract void Apply(AttackStatsMultipliers multipliers);

		public override bool TryStack(RuntimePerkModifier other)
		{
			if (!(other is WeaponStatsPerkModifier weaponStatsPerkModifier))
			{
				return false;
			}
			parameters.multiplierIncrement += weaponStatsPerkModifier.parameters.multiplierIncrement;
			return true;
		}
	}
}
