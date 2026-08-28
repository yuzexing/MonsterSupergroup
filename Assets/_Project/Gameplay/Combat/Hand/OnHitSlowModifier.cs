using AstralShift.HellMaiden.AI.Enemy;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("On Hit Slow")]
	public class OnHitSlowModifier : OnHitModifier
	{
		[EquipmentModifierParams]
		protected class Params : BaseParams
		{
			public float speedMultiplier;

			public float duration;
		}

		[InjectEquipmentModifierParams]
		protected Params parameters;

		public override float GetRollChance()
		{
			return parameters.chance;
		}

		public override float GetRollPriority()
		{
			return parameters.speedMultiplier * parameters.duration;
		}

		protected override OnHitModifierArgs ApplyEffect(OnHitModifierArgs args)
		{
			if (args.Enemy.IsAlive)
			{
				args.Enemy.status.Apply(EnemyStatusID.Slow, parameters.speedMultiplier, parameters.duration);
			}
			return args;
		}
	}
}
