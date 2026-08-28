using AstralShift.HellMaiden.AI.Enemy;
using UnityEngine;
using EnemyStatusID = MonsterSupergroup.GAS.EnemyStatusID;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("On Hit Poison")]
	public class OnHitPoisonModifier : OnHitModifier
	{
		[EquipmentModifierParams]
		protected class Params : BaseParams
		{
			public float damageMultiplier;

			public int numberOfHits;

			public float hitIntervalDuration;
		}

		[InjectEquipmentModifierParams]
		protected Params parameters;

		protected const int BASE_HEALTH_CAP = 200;

		public override float GetRollChance()
		{
			return parameters.chance;
		}

		public override float GetRollPriority()
		{
			return parameters.damageMultiplier * (float)parameters.numberOfHits * parameters.hitIntervalDuration;
		}

		protected override OnHitModifierArgs ApplyEffect(OnHitModifierArgs args)
		{
			if (args.Enemy.IsAlive)
			{
				int num = 0;
				num = ((!(args.Weapon.StatsBehaviour.PlayerStats.statMultipliers.attackStatsMultipliers.burnDamageMultiplier > 0f)) ? ((int)((float)Mathf.Min(200, args.Enemy.MaxHealth) * parameters.damageMultiplier)) : ((int)((float)Mathf.Min(200, args.Enemy.MaxHealth) * parameters.damageMultiplier * (1f + args.Weapon.StatsBehaviour.PlayerStats.statMultipliers.attackStatsMultipliers.poisonDamageMultiplier))));
				args.Enemy.status.Apply(
					EnemyStatusID.Poison,
					num,
					parameters.numberOfHits,
					parameters.hitIntervalDuration,
					source: args.Source);
			}
			return args;
		}
	}
}
