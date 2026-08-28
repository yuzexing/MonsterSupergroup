using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("On Kill Magnet")]
	public class OnKillMagnetModifier : OnKillModifier
	{
		[EquipmentModifierParams]
		protected class Params : BaseParams
		{
			public float pullRadius;
		}

		[InjectEquipmentModifierParams]
		protected Params parameters;

		public override float GetRollChance()
		{
			return parameters.chance;
		}

		public override float GetRollPriority()
		{
			return parameters.pullRadius;
		}

		public override OnKillModifierArgs ApplyEffect(OnKillModifierArgs args)
		{
			if (args.Enemy is EnemyController enemyController)
			{
				enemyController.OnDeathFinalized += delegate
				{
					FinallyApplyEffect(args.Enemy, args.Weapon);
				};
			}
			return args;
		}

		private void FinallyApplyEffect(BaseEnemyController enemy, WeaponBehaviour weapon)
		{
			if (enemy is EnemyController { SpawnedLoot: not false })
			{
				OnKillMagnetEffect effect = EquipmentEffectResolver.Instance.GetMagnetEffect();
				effect.transform.position = enemy.transform.position;
				effect.transform.SetParent(null);
				effect.transform.localScale = Vector3.one * parameters.pullRadius;
				effect.pullArea = parameters.pullRadius;
				effect.Init(weapon);
				effect.Play(delegate
				{
					OnEffectEnd(effect);
				});
			}
		}

		private void OnEffectEnd(OnKillMagnetEffect effect)
		{
			EquipmentEffectResolver.Instance.ReturnMagnetEffect(effect);
		}
	}
}
