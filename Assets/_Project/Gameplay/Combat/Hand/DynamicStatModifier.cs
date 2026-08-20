using System;
using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[Serializable]
	public abstract class DynamicStatModifier : RuntimeEquipmentModifier
	{
		protected class BaseParams
		{
			public float chance;
		}

		public virtual float GetChance()
		{
			return 1f;
		}

		public virtual void Apply(WeaponBehaviourStats stats, WeaponBehaviour weapon)
		{
			Apply(stats.DynamicStatsMultipliers, weapon);
		}

		public virtual void Apply(AttackStatsMultipliers multipliers, WeaponBehaviour weapon)
		{
		}
	}
}
