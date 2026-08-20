using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public abstract class EquipmentPerkModifier : RuntimePerkModifier
	{
		[PerkModifierParams]
		protected class ParamsData
		{
			public float onHitChanceMultiplier = 1f;

			public float onKillChanceMultiplier = 1f;
		}

		[InjectPerkModifierParams]
		protected ParamsData parameters;

		public abstract void Apply(PlayerStats.EquipmentStatsMultipliers multipliers);

		public override bool TryStack(RuntimePerkModifier other)
		{
			if (!(other is EquipmentPerkModifier equipmentPerkModifier))
			{
				return false;
			}
			parameters.onHitChanceMultiplier += equipmentPerkModifier.parameters.onHitChanceMultiplier - 1f;
			parameters.onKillChanceMultiplier += equipmentPerkModifier.parameters.onKillChanceMultiplier - 1f;
			return true;
		}
	}
}
