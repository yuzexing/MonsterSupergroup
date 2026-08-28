using AstralShift.HellMaiden.AI.Enemy;
using EnemyStatusID = MonsterSupergroup.GAS.EnemyStatusID;
using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("Extra Damage On Poison")]
	public class DynamicDamageOnPoisonModifier : DynamicOnDamageModifier
	{
		public override void Apply(AttackStatsMultipliers multipliers, BaseEnemyController enemy)
		{
			if (enemy.status.HasStatus(EnemyStatusID.Poison))
			{
				multipliers.damage += parameters.multiplierIncrement;
			}
		}
	}
}
