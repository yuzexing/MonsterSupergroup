using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("Status Stack Consume Chance")]
	public class StatusStackConsumeChanceModifier : DynamicOnDamageModifier
	{
		[EquipmentModifierParams]
		protected new class Params
		{
			public EnemyStatusID status;

			public float chance;
		}

		[InjectEquipmentModifierParams]
		protected new Params parameters;

		public override void Apply(AttackStatsMultipliers multipliers, BaseEnemyController enemy)
		{
			if (enemy.status.HasStatus(parameters.status) && Random.Range(0f, 1f) < parameters.chance)
			{
				enemy.status.ConsumeStack(parameters.status);
			}
		}
	}
}
