using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("Extra Projectiles Chance")]
	public class ExtraProjectilesChanceModifier : DynamicStatModifier
	{
		[EquipmentModifierParams]
		protected class Params
		{
			public float[] chances;

			public int[] projectileIncrement;
		}

		[InjectEquipmentModifierParams]
		protected Params parameters;

		private int GetProjectileCount()
		{
			for (int i = 0; i < parameters.chances.Length; i++)
			{
				if (Random.Range(0f, 1f) > parameters.chances[i])
				{
					return parameters.projectileIncrement[i];
				}
			}
			return parameters.projectileIncrement[^1];
		}

		public override void Apply(AttackStatsMultipliers multipliers, WeaponBehaviour weapon)
		{
			multipliers.projectileCountIncrement += GetProjectileCount();
		}
	}
}
