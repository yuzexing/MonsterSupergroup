using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public abstract class OnKillModifier : RuntimeEquipmentModifier
	{
		protected class BaseParams
		{
			public float chance;
		}

		public abstract float GetRollChance();

		public abstract float GetRollPriority();

		public OnKillModifierArgs Apply(OnKillModifierArgs args)
		{
			float rollChance = GetRollChance();
			float onKillChanceMultiplier = GameDirector.Instance.Player.PlayerStats.equipmentStatsMultipliers.OnKillChanceMultiplier;
			float num = rollChance * onKillChanceMultiplier;
			if (Random.value <= num)
			{
				args = ApplyEffect(args);
			}
			return args;
		}

		public abstract OnKillModifierArgs ApplyEffect(OnKillModifierArgs args);
	}
}
