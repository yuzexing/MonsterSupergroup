using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("On Hit Life Steal")]
	public class OnHitLifeStealModifier : OnHitModifier
	{
		[EquipmentModifierParams]
		protected class Params : BaseParams
		{
			[Tooltip("Percentage (in multiplier) of damage dealt converted into health")]
			public float dealtDamageToHealhMultiplier;
		}

		[InjectEquipmentModifierParams]
		protected Params parameters;

		public override float GetRollChance()
		{
			return parameters.chance;
		}

		public override float GetRollPriority()
		{
			return parameters.dealtDamageToHealhMultiplier;
		}

		protected override OnHitModifierArgs ApplyEffect(OnHitModifierArgs args)
		{
			args.Weapon?.OwnerCombatant?.RestoreHealth(
				Mathf.CeilToInt(
					(float)args.DamageInfo.value * parameters.dealtDamageToHealhMultiplier));
			return args;
		}
	}
}
