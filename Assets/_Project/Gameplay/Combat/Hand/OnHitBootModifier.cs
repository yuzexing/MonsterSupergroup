using System;
using AstralShift.FSM;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;
using CombatTags = MonsterSupergroup.GAS.CombatTags;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("On Hit Boot")]
	public class OnHitBootModifier : OnHitModifier
	{
		[EquipmentModifierParams]
		protected class Params : BaseParams
		{
			public float damageMultiplier;

			public float knockbackStrenght;

			public float knockbackTime;

			public AnimationCurve knockbackCurve;
		}

		[InjectEquipmentModifierParams]
		protected Params parameters;

		public override float GetRollChance()
		{
			return parameters.chance;
		}

		public override float GetRollPriority()
		{
			return 1f;
		}

		protected override OnHitModifierArgs ApplyEffect(OnHitModifierArgs args)
		{
			EnemyController enemyController;
			if (args.Enemy is EnemyController && args.Enemy.isActiveAndEnabled && args.Enemy.stats.KnockBackMultiplier != 0f)
			{
				enemyController = (EnemyController)args.Enemy;
				State knockback = enemyController.Knockback;
				knockback.onEnterOnce = (Action)Delegate.Combine(knockback.onEnterOnce, new Action(PlayEffect));
				KnockbackSettings knockbackSettings = ScriptableObject.CreateInstance<KnockbackSettings>();
				knockbackSettings.distance = parameters.knockbackStrenght;
				knockbackSettings.speedMultiplier = 1f / parameters.knockbackTime;
				knockbackSettings.speedCurve = parameters.knockbackCurve;
				args.Enemy.OverrideKnockbackSettings(knockbackSettings);
			}
			return args;
			void PlayEffect()
			{
				AttackHitParticleEffect effect = EquipmentEffectResolver.Instance.GetBootEffect();
				effect.transform.parent = args.Enemy.transform;
				effect.transform.localPosition = Vector3.zero;
				effect.Init(args.Weapon);
				int damageValue = (int)((float)args.Weapon.CalculateDamage(args.Enemy).value * parameters.damageMultiplier);
				effect.Play(delegate(IDamageable damageable)
				{
					LegacyDamageDispatcher.Damage(
						damageable,
						damageValue,
						DamageType.Normal,
						args.Source,
						CombatTags.Build);
				}, delegate
				{
					OnEffectEnd(effect);
				});
				State knockback2 = enemyController.Knockback;
				knockback2.onExitOnce = (Action)Delegate.Combine(knockback2.onExitOnce, new Action(StopEffect));
				void StopEffect()
				{
					effect.Stop();
				}
			}
		}

		private void OnEffectEnd(AttackHitParticleEffect effect)
		{
			EquipmentEffectResolver.Instance?.ReturnBootEffect(effect);
		}
	}
}
