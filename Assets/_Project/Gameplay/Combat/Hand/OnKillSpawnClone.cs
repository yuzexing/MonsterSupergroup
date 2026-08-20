using System;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.Pooling;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("On Kill Spawn Clone")]
	public class OnKillSpawnClone : OnKillModifier
	{
		[EquipmentModifierParams]
		protected class Params : BaseParams
		{
			public float explosionAreaRadius;

			public int damageValue;

			public EnemyAIAttractor clonePrefab;

			public AttackHitParticleEffect cloneExplosion;

			public bool alwaysExplodeOnDeath;

			public float explodeOnDeathChance;
		}

		[InjectEquipmentModifierParams]
		protected Params parameters;

		private GenericPooler<EnemyAIAttractor> _danteCloneAttractorPooler;

		private EnemyAIAttractor _activeClone;

		private Action _onDestroyedHandler;

		public override float GetRollChance()
		{
			return parameters.chance;
		}

		public override float GetRollPriority()
		{
			return parameters.damageValue;
		}

		public override OnKillModifierArgs ApplyEffect(OnKillModifierArgs args)
		{
			if (args.Enemy == null || args.Enemy.stats == null || args.Enemy.ID == -1 || args.Enemy.ID == -2 || IsCloneAlreadyActive())
			{
				return args;
			}
			parameters.cloneExplosion.Init(args.Weapon);
			if (args.Enemy is EnemyController)
			{
				Spawn(args);
			}
			return args;
		}

		private void Spawn(OnKillModifierArgs args)
		{
			Vector2 position = args.Enemy.transform.position;
			SpawnClone(position).Forget();
		}

		private async UniTaskVoid SpawnClone(Vector2 position)
		{
			if (_danteCloneAttractorPooler == null)
			{
				_danteCloneAttractorPooler = PoolManager.Instance.GetOrCreatePooler(parameters.clonePrefab);
			}
			EnemyAIAttractor clone = _danteCloneAttractorPooler.GetOrCreate(null, activate: true);
			clone.transform.position = position;
			_onDestroyedHandler = delegate
			{
				HandleCloneDeath(clone);
			};
			EnemyAIAttractor enemyAIAttractor = clone;
			enemyAIAttractor.OnEnemyAIAttractorDestroyed = (Action)Delegate.Combine(enemyAIAttractor.OnEnemyAIAttractorDestroyed, _onDestroyedHandler);
			_activeClone = clone;
			clone.Initialize();
			AutoReturnAfterDuration(clone, 10f).Forget();
		}

		private void HandleCloneDeath(EnemyAIAttractor clone)
		{
			if (!(clone == null))
			{
				if (_onDestroyedHandler != null)
				{
					clone.OnEnemyAIAttractorDestroyed = (Action)Delegate.Remove(clone.OnEnemyAIAttractorDestroyed, _onDestroyedHandler);
					_onDestroyedHandler = null;
				}
				if (ShouldExplode())
				{
					ExplodeClone(clone);
				}
				else if (_activeClone == clone)
				{
					_activeClone = null;
				}
			}
		}

		private bool ShouldExplode()
		{
			if (parameters.alwaysExplodeOnDeath)
			{
				return true;
			}
			float num = Mathf.Clamp01(parameters.explodeOnDeathChance);
			return UnityEngine.Random.value <= num;
		}

		private void ExplodeClone(EnemyAIAttractor clone)
		{
			if (clone == null)
			{
				return;
			}
			if (parameters.cloneExplosion == null)
			{
				Debug.LogWarning("Explosion effect is null, cannot explode clone");
				return;
			}
			GenericPooler<AttackHitParticleEffect> effect = PoolManager.Instance.GetOrCreatePooler(parameters.cloneExplosion);
			AttackHitParticleEffect explosion = effect.GetOrCreate(null, activate: true);
			explosion.transform.position = clone.transform.position;
			explosion.transform.localScale = Vector3.one * parameters.explosionAreaRadius;
			parameters.cloneExplosion.enabled = true;
			explosion.Play(delegate(IDamageable damageable)
			{
				damageable.Damage(parameters.damageValue, DamageType.Normal);
			}, ReturnToPool);
			void ReturnToPool()
			{
				effect.Return(explosion);
			}
		}

		private bool IsCloneAlreadyActive()
		{
			if (_activeClone != null)
			{
				return true;
			}
			EnemyAIAttractor existingAttractor = UnityEngine.Object.FindFirstObjectByType<EnemyAIAttractor>();
			if (existingAttractor != null)
			{
				_activeClone = existingAttractor;
				_onDestroyedHandler = delegate
				{
					HandleCloneDeath(existingAttractor);
				};
				EnemyAIAttractor enemyAIAttractor = existingAttractor;
				enemyAIAttractor.OnEnemyAIAttractorDestroyed = (Action)Delegate.Combine(enemyAIAttractor.OnEnemyAIAttractorDestroyed, _onDestroyedHandler);
				return true;
			}
			return false;
		}

		private async UniTaskVoid AutoReturnAfterDuration(EnemyAIAttractor attractor, float duration)
		{
			await UniTask.WaitForSeconds(duration);
			if (_activeClone == attractor && attractor != null)
			{
				HandleCloneDeath(attractor);
				ReturnToPool(attractor);
			}
		}

		private void ReturnToPool(EnemyAIAttractor attractor)
		{
			if (_danteCloneAttractorPooler != null && attractor != null)
			{
				if (_onDestroyedHandler != null)
				{
					attractor.OnEnemyAIAttractorDestroyed = (Action)Delegate.Remove(attractor.OnEnemyAIAttractorDestroyed, _onDestroyedHandler);
					_onDestroyedHandler = null;
				}
				attractor.ClearAllAffectedEnemies();
				_danteCloneAttractorPooler.Return(attractor);
				if (_activeClone == attractor)
				{
					_activeClone = null;
				}
			}
		}
	}
}
