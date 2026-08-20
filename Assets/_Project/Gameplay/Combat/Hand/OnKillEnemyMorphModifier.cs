using System;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.Pooling;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("On Kill Enemy To Damage Effect Morph")]
	public class OnKillEnemyMorphModifier : OnKillModifier
	{
		[EquipmentModifierParams]
		protected class Params : BaseParams
		{
			public AttackHitParticleEffect morphEffectPrefab;

			public AttackHitParticleEffect damageablePrefab;

			public float damageMultiplier;

			public float duration;
		}

		[InjectEquipmentModifierParams]
		protected Params parameters;

		private GenericPooler<AttackHitParticleEffect> _morphEffectPooler;

		private GenericPooler<AttackHitParticleEffect> _damageablePool;

		public override int GetSortPriority()
		{
			return int.MaxValue;
		}

		public override float GetRollChance()
		{
			return parameters.chance;
		}

		public override float GetRollPriority()
		{
			return parameters.chance;
		}

		public override OnKillModifierArgs ApplyEffect(OnKillModifierArgs args)
		{
			if (args.Enemy.ID == -1)
			{
				return args;
			}
			if (!(args.Enemy is EnemyController enemyController))
			{
				return args;
			}
			enemyController.OnKill += Spawn;
			return args;
			void Spawn()
			{
				SpawnEffect(args);
			}
		}

		private void SpawnEffect(OnKillModifierArgs args)
		{
			Vector2 position = args.Enemy.transform.position;
			SpawnMorphEffect(position);
			SpawnDamageable(args, position);
		}

		private void SpawnMorphEffect(Vector2 position)
		{
			if (_morphEffectPooler == null)
			{
				_morphEffectPooler = PoolManager.Instance.GetOrCreatePooler(parameters.morphEffectPrefab);
			}
			AttackHitParticleEffect particleEffect = _morphEffectPooler.GetOrCreate(null, activate: true);
			particleEffect.transform.position = position;
			particleEffect.Play(ReturnToPool);
			void ReturnToPool()
			{
				_morphEffectPooler.Return(particleEffect);
			}
		}

		private void SpawnDamageable(OnKillModifierArgs args, Vector2 position)
		{
			if (_damageablePool == null)
			{
				_damageablePool = PoolManager.Instance.GetOrCreatePooler(parameters.damageablePrefab);
			}
			AttackHitParticleEffect damageable = _damageablePool.GetOrCreate(null, activate: true);
			damageable.transform.position = position;
			damageable.Init(args.Weapon);
			SetDuration(damageable.system, parameters.duration);
			int damageValue = (int)((float)args.Weapon.CalculateDamage(args.Enemy).value * parameters.damageMultiplier);
			damageable.Play(delegate(IDamageable damageable2)
			{
				damageable2.Damage(damageValue, DamageType.Normal);
			}, ReturnToPool);
			WaitAndStop(damageable, parameters.duration).Forget();
			void ReturnToPool()
			{
				_damageablePool.Return(damageable);
			}
		}

		private void SetDuration(ParticleSystem particleSystem, float duration)
		{
			ParticleSystem.MainModule main = particleSystem.main;
			main.startLifetime = duration;
		}

		private async UniTaskVoid WaitAndStop(AttackHitParticleEffect effect, float duration)
		{
			await UniTask.Delay(TimeSpan.FromSeconds(duration));
			if (effect != null)
			{
				effect.Stop();
			}
		}
	}
}
