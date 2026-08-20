using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("Player Contact Based Damage")]
	public class PlayerContactBasedDamagePerkModifier : PlayerPerkModifier
	{
		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.attackStatsMultipliers.contactDamageReceivedMultiplier += parameters.multiplierIncrement;
		}
	}
}
