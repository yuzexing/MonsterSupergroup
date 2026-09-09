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
		private Transform _checkoutRoot;

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
			if (_hitEffectPooler == null) return;

			// The resolver can be reused while a previous impact is still alive.
			// Its callbacks must own that spawn's source, snapshot and pool.
			WeaponBehaviour source = _behaviour;
			GenericPooler<BaseAttackHitEffect> pool = _hitEffectPooler;
			AttackSnapshotLease attackLease = attack?.Retain();
			BaseAttackHitEffect effect = null;
			bool completed = false;
			Action onEnd = () =>
			{
				if (completed) return;
				completed = true;
				attackLease?.Dispose();
				if (!effect) return;
				// Unity forbids reparenting from an external OnDisable/OnDestroy stack.
				// Retire cancelled instances; normal completion still returns to the pool.
				if (effect.isActiveAndEnabled) pool.Return(effect);
				else Destroy(effect.gameObject);
			};
			Action<IDamageable> onHit = damageable =>
			{
				if (completed || !source || !effect || damageable == null) return;
				if (attackLease != null)
				{
					if (source.NativeRuntime != null && source.NativeRuntime.IsInitialized)
						source.OnNativeGasHit(effect.transform.position, damageable, attackLease.Snapshot);
				}
				else source.OnHit(effect.transform.position, damageable);
			};
			try
			{
				if (_checkoutRoot == null)
				{
					var staging = new GameObject("Impact Initialization");
					staging.SetActive(false);
					staging.transform.SetParent(transform, false);
					_checkoutRoot = staging.transform;
				}
				effect = pool.GetOrCreate(_checkoutRoot, activate: false);
				effect.gameObject.SetActive(false);
				effect.transform.SetParent(null, false);
				effect.transform.position = hitEffectSpawnPivot
					? new Vector3(hitEffectSpawnPivot.position.x, hitEffectSpawnPivot.position.y, 0f)
					: transform.position;
				effect.transform.rotation = hitEffect.transform.rotation;
				effect.transform.localScale = hitEffect.transform.localScale;
				if (damageMode == DamageMode.ExplosionHit || damageMode == DamageMode.Both || damageMode == DamageMode.MainHit)
				{
					if (attack != null) effect.Init(source, attack);
					else effect.Init(source);
				}
				// Init precedes OnEnable even when Instantiate receives an active prefab.
				effect.gameObject.SetActive(true);
				switch (damageMode)
				{
				case DamageMode.ExplosionHit:
				case DamageMode.Both:
					effect.PlayOnEnable(onHit, onEnd);
					break;
				default:
					effect.PlayOnEnable(onEnd);
					break;
				}
			}
			catch
			{
				onEnd();
				throw;
			}
		}
	}
}
