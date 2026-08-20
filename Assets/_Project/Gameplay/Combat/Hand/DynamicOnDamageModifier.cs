using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public abstract class DynamicOnDamageModifier : RuntimeEquipmentModifier
	{
		[EquipmentModifierParams]
		protected class Params
		{
			public float multiplierIncrement;
		}

		[InjectEquipmentModifierParams]
		protected Params parameters;

		public abstract void Apply(AttackStatsMultipliers multipliers, BaseEnemyController enemy);
	}
}
