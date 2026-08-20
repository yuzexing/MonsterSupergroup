using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("Player Taken Damage")]
	public class PlayerTakenDamagePerkModifier : PlayerPerkModifier
	{
		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.receivedDamageMultiplier += parameters.multiplierIncrement;
		}
	}
}
