using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("On Status InstaKill Chance")]
	public class OnStatusInstaKillChanceModifier : DynamicOnDamageModifier
	{
		[EquipmentModifierParams]
		protected new class Params
		{
			public EnemyStatusID status;

			public float chance;

			public float enemyHealthPercentageMultiplier;
		}

		[InjectEquipmentModifierParams]
		protected new Params parameters;

		private float GetThreshold(BaseEnemyController enemy)
		{
			return (float)enemy.stats.BaseHealth * parameters.enemyHealthPercentageMultiplier;
		}

		private DamageType GetDamageType(EnemyStatusID status)
		{
			return status switch
			{
				EnemyStatusID.Slow => DamageType.Normal, 
				EnemyStatusID.Burn => DamageType.Fire, 
				EnemyStatusID.Poison => DamageType.Poison, 
				EnemyStatusID.Bleed => DamageType.Bleed, 
				EnemyStatusID.Weaken => DamageType.Normal, 
				_ => DamageType.Normal, 
			};
		}

		public override void Apply(AttackStatsMultipliers multipliers, BaseEnemyController enemy)
		{
			if (enemy.status.HasStatus(parameters.status) && Random.Range(0f, 1f) < parameters.chance && (float)enemy.stats.Health <= GetThreshold(enemy))
			{
				enemy.Damage(enemy.stats.BaseHealth, GetDamageType(parameters.status));
			}
		}
	}
}
