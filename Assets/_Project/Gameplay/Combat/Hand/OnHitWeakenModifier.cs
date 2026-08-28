using AstralShift.HellMaiden.AI.Enemy;
using EnemyStatusID = MonsterSupergroup.GAS.EnemyStatusID;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("On Hit Weaken")]
	public class OnHitWeakenModifier : OnHitModifier
	{
		[EquipmentModifierParams]
		protected class Params : BaseParams
		{
			public float damageMultiplier;

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
			return parameters.damageMultiplier * parameters.duration;
		}

		protected override OnHitModifierArgs ApplyEffect(OnHitModifierArgs args)
		{
			if (args.Enemy.IsAlive)
			{
				args.Enemy.status.Apply(
					EnemyStatusID.Weaken,
					parameters.damageMultiplier,
					parameters.duration,
					source: args.Source);
			}
			return args;
		}
	}
}
