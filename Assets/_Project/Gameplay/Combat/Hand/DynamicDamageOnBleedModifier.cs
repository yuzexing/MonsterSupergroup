using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("Extra Damage On Bleed")]
	public class DynamicDamageOnBleedModifier : DynamicOnDamageModifier
	{
		public override void Apply(AttackStatsMultipliers multipliers, BaseEnemyController enemy)
		{
			if (enemy.status.HasStatus(EnemyStatusID.Bleed))
			{
				multipliers.damage += parameters.multiplierIncrement;
			}
		}
	}
}
