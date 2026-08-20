using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("Extra Damage On Lower Health.")]
	public class DynamicLowHealthDamageBoost : DynamicOnDamageModifier
	{
		[EquipmentModifierParams]
		private new class Params
		{
			public float maxMultiplier = 1f;

			public float minThreshold = 0.7f;

			public float maxThreshold = 0.8f;
		}

		[InjectEquipmentModifierParams]
		private Params _parameters;

		public override void Apply(AttackStatsMultipliers multipliers, BaseEnemyController enemy)
		{
			int hP = GameDirector.Instance.Player.PlayerStats.currentStats.HP;
			int maxHP = GameDirector.Instance.Player.PlayerStats.currentStats.maxHP;
			float num = hP / maxHP;
			if (!(num >= _parameters.maxThreshold))
			{
				if (num <= _parameters.minThreshold)
				{
					multipliers.damage += _parameters.maxMultiplier;
					return;
				}
				float t = (num - _parameters.minThreshold) / (_parameters.maxThreshold - _parameters.minThreshold);
				float num2 = Mathf.Lerp(1f, _parameters.maxMultiplier, t);
				multipliers.damage += num2;
			}
		}
	}
}
