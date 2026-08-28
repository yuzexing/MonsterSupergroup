using AstralShift.HellMaiden.AI.Enemy;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("On Hit Bleed")]
	public class OnHitBleedModifier : OnHitModifier
	{
		[EquipmentModifierParams]
		protected class Params : BaseParams
		{
			public float damageValue;

			public int numberOfHits;

			public float hitIntervalDuration;
		}

		[InjectEquipmentModifierParams]
		protected Params parameters;

		public override float GetRollChance()
		{
			return parameters.chance;
		}

		public override float GetRollPriority()
		{
			return parameters.damageValue * (float)parameters.numberOfHits * parameters.hitIntervalDuration;
		}

		protected override OnHitModifierArgs ApplyEffect(OnHitModifierArgs args)
		{
			if (args.Enemy.IsAlive)
			{
				int num = 0;
				num = ((!(args.Weapon.StatsBehaviour.PlayerStats.statMultipliers.attackStatsMultipliers.bleedDamageMultiplier > 0f)) ? ((int)parameters.damageValue) : ((int)(parameters.damageValue * (1f + args.Weapon.StatsBehaviour.PlayerStats.statMultipliers.attackStatsMultipliers.bleedDamageMultiplier))));
				args.Enemy.status.Apply(
					EnemyStatusID.Bleed,
					num,
					parameters.numberOfHits,
					parameters.hitIntervalDuration,
					GetRollPriority(),
					args.Source);
			}
			return args;
		}
	}
}
