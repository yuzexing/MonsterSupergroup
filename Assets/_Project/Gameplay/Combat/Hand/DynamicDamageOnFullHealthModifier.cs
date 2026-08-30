using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("Extra Damage On Full Health")]
	public class DynamicDamageOnFullHealthModifier : DynamicOnDamageModifier
	{
		public override void Apply(AttackStatsMultipliers multipliers, BaseEnemyController enemy)
		{
			PlayerCombatantBinding owner = GetSourceSlot()?.WeaponBehaviour?.OwnerCombatant;
			if (owner != null && owner.CurrentHealth == owner.MaximumHealth)
			{
				multipliers.damage += parameters.multiplierIncrement;
			}
		}
	}
}
