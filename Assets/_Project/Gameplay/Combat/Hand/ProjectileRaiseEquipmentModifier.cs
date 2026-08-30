using System;
using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("Projectile Count")]
	public class ProjectileRaiseEquipmentModifier : StaticStatModifier
	{
		[Serializable]
		[EquipmentModifierParams]
		protected class Params
		{
			public int countIncrement;
		}

		[InjectEquipmentModifierParams]
		protected new Params parameters;

		public override void Apply(AttackStatsMultipliers multipliers)
		{
			multipliers.projectileCountIncrement += parameters.countIncrement;
		}
	}
}
