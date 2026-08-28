using AstralShift.HellMaiden.AI.Enemy;
using EnemyStatusID = MonsterSupergroup.GAS.EnemyStatusID;
using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("Extra Damage On Burn")]
	public class DynamicDamageOnBurnModifier : DynamicOnDamageModifier
	{
		public override void Apply(AttackStatsMultipliers multipliers, BaseEnemyController enemy)
		{
			if (enemy.status.HasStatus(EnemyStatusID.Burn))
			{
				multipliers.damage += parameters.multiplierIncrement;
			}
		}
	}
}
