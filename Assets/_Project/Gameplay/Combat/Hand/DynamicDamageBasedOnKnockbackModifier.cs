using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("Extra Damage Based On Knockback")]
	public class DynamicDamageBasedOnKnockbackModifier : DynamicStatModifier
	{
		[EquipmentModifierParams]
		protected class Params
		{
			public float damageMultiplier;
		}

		[InjectEquipmentModifierParams]
		protected Params parameters;

		public override void Apply(AttackStatsMultipliers multipliers, WeaponBehaviour weapon)
		{
			if (weapon.KnockbackSettings.HasKnockback)
			{
				multipliers.damage += weapon.KnockBackDistance * parameters.damageMultiplier;
			}
		}
	}
}
