using AstralShift.HellMaiden.AI.Enemy;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("On Hit Burn")]
	public class OnHitBurnModifier : OnHitModifier
	{
		[EquipmentModifierParams]
		public class Params : BaseParams
		{
			public float damageMultiplier;

			public int numberOfHits;

			public float hitIntervalDuration;
		}

		[InjectEquipmentModifierParams]
		public Params parameters;

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
			if (args.Enemy.stats.Health > 0)
			{
				int num = 0;
				num = ((!(args.Weapon.StatsBehaviour.PlayerStats.statMultipliers.attackStatsMultipliers.burnDamageMultiplier > 0f)) ? ((int)((float)args.Weapon.DamageValue * parameters.damageMultiplier)) : ((int)((float)args.Weapon.DamageValue * parameters.damageMultiplier * (1f + args.Weapon.StatsBehaviour.PlayerStats.statMultipliers.attackStatsMultipliers.burnDamageMultiplier))));
				args.Enemy.status.Apply(EnemyStatusID.Burn, num, parameters.numberOfHits, parameters.hitIntervalDuration);
			}
			return args;
		}
	}
}
