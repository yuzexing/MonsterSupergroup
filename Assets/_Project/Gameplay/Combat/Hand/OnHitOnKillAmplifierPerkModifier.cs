using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("OnHitOnKill Chance Multiplier")]
	public class OnHitOnKillAmplifierPerkModifier : EquipmentPerkModifier
	{
		public override void Apply(PlayerStats.EquipmentStatsMultipliers multipliers)
		{
			multipliers.OnHitChanceMultiplier += parameters.onHitChanceMultiplier;
			multipliers.OnKillChanceMultiplier += parameters.onKillChanceMultiplier;
		}
	}
}
