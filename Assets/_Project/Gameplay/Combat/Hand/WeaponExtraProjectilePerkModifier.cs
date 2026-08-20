using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("Projectile Increment")]
	public class WeaponExtraProjectilePerkModifier : PlayerPerkModifier
	{
		[PerkModifierParams]
		protected new class ParamsData
		{
			public int projectileIncrement;
		}

		[InjectPerkModifierParams]
		protected new ParamsData parameters;

		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.attackStatsMultipliers.projectileCountIncrement += parameters.projectileIncrement;
		}
	}
}
