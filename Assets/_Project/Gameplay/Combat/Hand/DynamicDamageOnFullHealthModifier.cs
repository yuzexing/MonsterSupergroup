using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("Extra Damage On Full Health")]
	public class DynamicDamageOnFullHealthModifier : DynamicOnDamageModifier
	{
		public override void Apply(AttackStatsMultipliers multipliers, BaseEnemyController enemy)
		{
			if (GameDirector.Instance.Player.PlayerStats.currentStats.HP == GameDirector.Instance.Player.PlayerStats.currentStats.maxHP)
			{
				multipliers.damage += parameters.multiplierIncrement;
			}
		}
	}
}
