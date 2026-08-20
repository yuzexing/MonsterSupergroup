using System.Collections;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("On Hit Chain Lightning")]
	public class OnHitChainLightningModifier : OnHitModifier
	{
		[EquipmentModifierParams]
		protected class Params : BaseParams
		{
			public float damageMultiplier;

			public float maxChains;

			public float chainRadius;

			public float lightningRate;
		}

		[InjectEquipmentModifierParams]
		protected Params parameters;

		private Transform _lightningTransformStart;

		private Transform _lightningTransformEnd;

		private readonly LayerMask _enemyLayer = LayerMask.GetMask("EnemyCollision");

		private int _guardDepth;

		private WaitForSeconds _lightningRateYield;

		private Collider2D[] _hits;

		public override float GetRollChance()
		{
			return parameters.chance;
		}

		public override float GetRollPriority()
		{
			return parameters.damageMultiplier * parameters.maxChains * parameters.lightningRate * parameters.chainRadius;
		}

		protected override OnHitModifierArgs ApplyEffect(OnHitModifierArgs args)
		{
			_lightningRateYield = new WaitForSeconds(parameters.lightningRate);
			if (_guardDepth > 0)
			{
				return args;
			}
			EquipmentEffectResolver.Instance.StartCoroutine(ChainWrapper(args.Enemy, args.Weapon));
			return args;
		}

		private IEnumerator ChainWrapper(BaseEnemyController enemy, WeaponBehaviour weapon)
		{
			_guardDepth++;
			try
			{
				yield return ChainVisualRoutine(enemy.transform, weapon);
			}
			finally
			{
				_guardDepth--;
			}
		}

		private IEnumerator ChainVisualRoutine(Transform origin, WeaponBehaviour weapon)
		{
			Transform current = origin;
			HashSet<Transform> visited = new HashSet<Transform> { current };
			for (int i = 0; (float)i < parameters.maxChains; i++)
			{
				Transform transform = FindClosestEnemy(current, parameters.chainRadius, visited);
				if (!transform)
				{
					break;
				}
				visited.Add(transform);
				ChainLightningHitEffect effect;
				if ((bool)current && (bool)transform)
				{
					effect = EquipmentEffectResolver.Instance.GetLightningEffect();
					effect.transform.parent = null;
					effect.transform.position = current.position;
					effect.Init(weapon);
					effect.SetTesla(current.transform, transform.transform);
					effect.Play(ReturnToPool);
				}
				if (transform.transform.TryGetComponent<EnemyController>(out var component))
				{
					EnemyHurtbox hurtBox = component.hurtBox;
					if (hurtBox.TryGetComponent<IDamageable>(out var component2))
					{
						int value = Mathf.Clamp((int)((float)weapon.DamageValue * parameters.damageMultiplier), 1, int.MaxValue);
						component2.Damage(value, DamageType.Lightning);
					}
					current = transform;
					yield return _lightningRateYield;
					continue;
				}
				break;
				void ReturnToPool()
				{
					EquipmentEffectResolver.Instance.ReturnLightningEffect(effect);
				}
			}
		}

		private Transform FindClosestEnemy(Transform origin, float radius, HashSet<Transform> visited)
		{
			_hits = Physics2D.OverlapCircleAll(origin.position, radius, _enemyLayer);
			Transform result = null;
			float num = float.PositiveInfinity;
			Collider2D[] hits = _hits;
			for (int i = 0; i < hits.Length; i++)
			{
				BaseEnemyController componentInParent = hits[i].GetComponentInParent<BaseEnemyController>();
				if (!componentInParent)
				{
					continue;
				}
				Transform transform = componentInParent.transform;
				if (!visited.Contains(transform))
				{
					float num2 = Vector2.Distance(origin.position, transform.position);
					if (num2 < num)
					{
						num = num2;
						result = transform;
					}
				}
			}
			return result;
		}
	}
}
