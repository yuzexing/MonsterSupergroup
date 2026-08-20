using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public abstract class OnHitModifier : RuntimeEquipmentModifier
	{
		public class BaseParams
		{
			public float chance;
		}

		public new virtual int GetSortPriority()
		{
			return 1;
		}

		public abstract float GetRollChance();

		public abstract float GetRollPriority();

		public OnHitModifierArgs Apply(OnHitModifierArgs args)
		{
			if (!args.Enemy)
			{
				return args;
			}
			float rollChance = GetRollChance();
			float onHitChanceMultiplier = GameDirector.Instance.Player.PlayerStats.equipmentStatsMultipliers.OnHitChanceMultiplier;
			float num = rollChance * onHitChanceMultiplier;
			if (Random.Range(0f, 1f) < num)
			{
				args = ApplyEffect(args);
			}
			return args;
		}

		protected abstract OnHitModifierArgs ApplyEffect(OnHitModifierArgs args);
	}
}
