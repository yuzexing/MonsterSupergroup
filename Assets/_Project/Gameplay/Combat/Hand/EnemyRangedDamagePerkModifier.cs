using AstralShift.HellMaiden.Player;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[PerkModifierType("On Ranged Enemy Damage")]
	public class EnemyRangedDamagePerkModifier : EnemyConditionPerkModifier
	{
		public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers)
		{
			multipliers.attackStatsMultipliers.rangedDamageMultiplier += parameters.multiplierIncrement;
		}
	}
}
