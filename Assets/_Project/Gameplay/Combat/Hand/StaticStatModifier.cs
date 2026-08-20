using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public abstract class StaticStatModifier : RuntimeEquipmentModifier
	{
		[EquipmentModifierParams]
		protected class ParamsData
		{
			public float multiplierIncrement;
		}

		[InjectEquipmentModifierParams]
		protected ParamsData parameters;

		public virtual void Apply(WeaponBehaviourStats stats)
		{
			Apply(stats.BaseStatsMultipliers);
		}

		public abstract void Apply(AttackStatsMultipliers multipliers);
	}
}
