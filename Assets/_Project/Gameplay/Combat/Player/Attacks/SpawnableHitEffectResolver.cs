using System;
using AstralShift.HellMaiden.Combat;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class SpawnableHitEffectResolver : BaseAttackHitEffectResolver
	{
		[SerializeField]
		private BaseAttackHitEffect hitEffect;

		[Tooltip("Optional. Defaults to transform.position.")]
		[SerializeField]
		private Transform hitEffectSpawnPivot;

		protected GenericPooler<BaseAttackHitEffect> _hitEffectPooler;

		private WeaponBehaviour _behaviour;

		public BaseAttackHitEffect HitEffect => hitEffect;

		public override void Initialize(WeaponBehaviour behaviour)
		{
			_behaviour = behaviour;
			if ((bool)hitEffect && _hitEffectPooler == null)
			{
				_hitEffectPooler = PoolManager.Instance.GetOrCreatePooler(hitEffect);
			}
			SpawnHitEffect();
		}

		private void OnDestroy()
		{
			_hitEffectPooler = null;
		}

		public void OnHit(IDamageable damageable)
		{
			if ((bool)_behaviour && damageable != null)
			{
				_behaviour.OnHit(base.transform.position, damageable);
			}
		}

		protected virtual void SpawnHitEffect()
		{
			if (_hitEffectPooler != null)
			{
				BaseAttackHitEffect effect = _hitEffectPooler.GetOrCreate(null, activate: true);
				if ((bool)hitEffectSpawnPivot)
				{
					effect.transform.position = new Vector3(hitEffectSpawnPivot.position.x, hitEffectSpawnPivot.position.y, 0f);
				}
				else
				{
					effect.transform.position = base.transform.position;
				}
				Action onEnd = delegate
				{
					_hitEffectPooler?.Return(effect);
				};
				switch (damageMode)
				{
				case DamageMode.ExplosionHit:
				case DamageMode.Both:
					effect.Init(_behaviour);
					effect.PlayOnEnable(OnHit, onEnd);
					break;
				case DamageMode.MainHit:
					effect.Init(_behaviour);
					effect.PlayOnEnable(onEnd);
					break;
				default:
					effect.PlayOnEnable(onEnd);
					break;
				}
			}
		}
	}
}
