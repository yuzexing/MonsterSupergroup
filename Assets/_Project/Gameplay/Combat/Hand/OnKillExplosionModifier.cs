using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;
using CombatTags = MonsterSupergroup.GAS.CombatTags;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("On Kill Explosion")]
	public class OnKillExplosionModifier : OnKillModifier
	{
		[EquipmentModifierParams]
		protected class Params : BaseParams
		{
			public float damageMultiplier;

			public float areaRadius;
		}

		[InjectEquipmentModifierParams]
		protected Params parameters;

		public override float GetRollChance()
		{
			return parameters.chance;
		}

		public override float GetRollPriority()
		{
			return parameters.damageMultiplier * parameters.areaRadius;
		}

		public override OnKillModifierArgs ApplyEffect(OnKillModifierArgs args)
		{
			AttackHitParticleEffect effect = EquipmentEffectResolver.Instance.GetExplosionEffect();
			effect.transform.position = args.Enemy.transform.position;
			effect.transform.localScale = Vector3.one * parameters.areaRadius;
			effect.Init(args.Weapon);
			effect.Play(delegate(IDamageable damageable)
			{
				LegacyDamageDispatcher.Damage(
					damageable,
					(int)((float)args.Weapon.CalculateDamage(args.Enemy).value * parameters.damageMultiplier),
					DamageType.Normal,
					args.Source,
					CombatTags.Build | CombatTags.Explosion);
			}, delegate
			{
				OnEffectEnd(effect);
			});
			return args;
		}

		private void OnEffectEnd(AttackHitParticleEffect effect)
		{
			EquipmentEffectResolver.Instance.ReturnExplosionEffect(effect);
		}
	}
}
