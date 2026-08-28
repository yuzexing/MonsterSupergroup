using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;
using CombatTags = MonsterSupergroup.GAS.CombatTags;

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
			return (float)enemy.MaxHealth * parameters.enemyHealthPercentageMultiplier;
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
			if (enemy.status.HasStatus(parameters.status) && Random.Range(0f, 1f) < parameters.chance && (float)enemy.CurrentHealth <= GetThreshold(enemy))
			{
				DamageType damageType = GetDamageType(parameters.status);
				WeaponBehaviour sourceWeapon = GetSourceSlot()?.WeaponBehaviour;
				if (enemy is EnemyController normalEnemy && sourceWeapon != null)
				{
					normalEnemy.Damage(
						enemy.MaxHealth,
						damageType,
						sourceWeapon.GetDamageSource(
							CombatTags.Build | CombatTags.Status));
				}
				else
				{
					enemy.Damage(enemy.MaxHealth, damageType);
				}
			}
		}
	}
}
