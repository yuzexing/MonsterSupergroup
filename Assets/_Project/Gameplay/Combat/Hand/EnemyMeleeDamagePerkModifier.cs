using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("On Melee Enemy Damage")]
	public class EnemyMeleeDamagePerkModifier : EnemyConditionPerkModifier
	{
		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.attackStatsMultipliers.meleeDamageMultiplier += parameters.multiplierIncrement;
		}
	}
}
