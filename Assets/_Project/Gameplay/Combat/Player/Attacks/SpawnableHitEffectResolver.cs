using System;
using AstralShift.HellMaiden.Combat;
using AstralShift.Pooling;
using MonsterSupergroup.GAS;
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
			EnsurePool();
			SpawnHitEffect(null);
		}

		public override void Initialize(
			WeaponBehaviour behaviour,
			AttackSnapshot attack)
		{
			_behaviour = behaviour;
			if (attack == null)
			{
				throw new ArgumentNullException(nameof(attack));
			}

			EnsurePool();
			SpawnHitEffect(attack);
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

		private void EnsurePool()
		{
			if ((bool)hitEffect && _hitEffectPooler == null)
			{
				_hitEffectPooler = PoolManager.Instance.GetOrCreatePooler(hitEffect);
			}
		}

		protected virtual void SpawnHitEffect(AttackSnapshot attack)
		{
			if (_hitEffectPooler != null)
			{
				AttackSnapshotLease attackLease = attack?.Retain();
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
					attackLease?.Dispose();
					_hitEffectPooler?.Return(effect);
				};
				Action<IDamageable> onHit = attackLease == null
					? OnHit
					: damageable =>
					{
						if ((bool)_behaviour && damageable != null)
						{
							_behaviour.OnNativeGasHit(
								effect.transform.position,
								damageable,
								attackLease.Snapshot);
						}
					};
				try
				{
				switch (damageMode)
				{
				case DamageMode.ExplosionHit:
				case DamageMode.Both:
					if (attack != null)
					{
						effect.Init(_behaviour, attack);
					}
					else
					{
						effect.Init(_behaviour);
					}
					effect.PlayOnEnable(onHit, onEnd);
					break;
				case DamageMode.MainHit:
					if (attack != null)
					{
						effect.Init(_behaviour, attack);
					}
					else
					{
						effect.Init(_behaviour);
					}
					effect.PlayOnEnable(onEnd);
					break;
				default:
					effect.PlayOnEnable(onEnd);
					break;
				}
				}
				catch
				{
					attackLease?.Dispose();
					_hitEffectPooler?.Return(effect);
					throw;
				}
			}
		}
	}
}
