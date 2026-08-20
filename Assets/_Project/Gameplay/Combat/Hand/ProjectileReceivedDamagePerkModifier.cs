using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("Projectile Received Damage")]
	public class ProjectileReceivedDamagePerkModifier : PlayerPerkModifier
	{
		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.attackStatsMultipliers.projectileDamageReceivedMultiplier += parameters.multiplierIncrement;
		}
	}
}
