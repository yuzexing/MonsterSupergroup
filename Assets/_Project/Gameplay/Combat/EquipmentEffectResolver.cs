using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat
{
	public class EquipmentEffectResolver : MonoBehaviour
	{
		public OnKillMagnetEffect MagnetPrefab;

		private GenericPooler<OnKillMagnetEffect> _magnetPool;

		public AttackHitParticleEffect ExplosionPrefab;

		private GenericPooler<AttackHitParticleEffect> _explosionPool;

		public ChainLightningHitEffect LightingPrefab;

		private GenericPooler<ChainLightningHitEffect> _lightningPool;

		public AttackHitParticleEffect bootPrefab;

		private GenericPooler<AttackHitParticleEffect> _bootPool;

		public static EquipmentEffectResolver Instance { get; private set; }

		public void Init()
		{
			if (Instance == null)
			{
				Instance = this;
			}
			CreateMagnetEffectPool();
			CreateExplosionEffectPool();
			CreateLightningEffectPool();
			CreateBootEffectPool();
		}

		private void CreateMagnetEffectPool()
		{
			_magnetPool = PoolManager.Instance.GetOrCreatePooler(MagnetPrefab);
		}

		public OnKillMagnetEffect GetMagnetEffect()
		{
			return _magnetPool.GetOrCreate(activate: true);
		}

		public void ReturnMagnetEffect(OnKillMagnetEffect magnetEffect)
		{
			_magnetPool?.Return(magnetEffect);
		}

		private void CreateExplosionEffectPool()
		{
			_explosionPool = PoolManager.Instance.GetOrCreatePooler(ExplosionPrefab);
		}

		public AttackHitParticleEffect GetExplosionEffect()
		{
			return _explosionPool.GetOrCreate(activate: true);
		}

		public void ReturnExplosionEffect(AttackHitParticleEffect effect)
		{
			_explosionPool?.Return(effect);
		}

		private void CreateLightningEffectPool()
		{
			_lightningPool = PoolManager.Instance.GetOrCreatePooler(LightingPrefab);
		}

		public ChainLightningHitEffect GetLightningEffect()
		{
			return _lightningPool.GetOrCreate(activate: true);
		}

		public void ReturnLightningEffect(ChainLightningHitEffect effect)
		{
			_lightningPool?.Return(effect);
		}

		private void CreateBootEffectPool()
		{
			_bootPool = PoolManager.Instance.GetOrCreatePooler(bootPrefab);
		}

		public AttackHitParticleEffect GetBootEffect()
		{
			return _bootPool.GetOrCreate(activate: true);
		}

		public void ReturnBootEffect(AttackHitParticleEffect effect)
		{
			_bootPool?.Return(effect);
		}
	}
}
