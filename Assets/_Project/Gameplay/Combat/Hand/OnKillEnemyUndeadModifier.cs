using System;
using System.Collections.Generic;
using Animancer;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Helpers;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.HellMaiden.Scenes;
using AstralShift.Pooling;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("On Kill Spawn Undead")]
	public class OnKillEnemyUndeadModifier : OnKillModifier
	{
		[EquipmentModifierParams]
		protected class Params : BaseParams
		{
			public bool affectElites;

			public AttackHitParticleEffect spawnEffectPrefab;

			[Space]
			public EnemyController enemyPrefab;

			public int variantIdx;

			[SerializeField]
			private ClipTransition upLeftTransition;

			[SerializeField]
			public ClipTransition upRightTransition;

			[SerializeField]
			private ClipTransition downLeftTransition;

			[SerializeField]
			private ClipTransition downRightTransition;

			[Space]
			public float lifeDuration = 10f;

			public int damageValue;

			public ClipTransition GetSpawnAnimationClip(Vector2 direction)
			{
				if (direction.x > 0f)
				{
					if (!(direction.y > 0f))
					{
						return downRightTransition;
					}
					return upRightTransition;
				}
				if (!(direction.y > 0f))
				{
					return downLeftTransition;
				}
				return upLeftTransition;
			}
		}

		private struct UndeadEntry
		{
			public EnemyController Undead;

			public EnemyControllerTargetOverrider TargetOverrider;

			public float Duration;

			public UndeadEntry(EnemyController undead, EnemyControllerTargetOverrider targetOverrider, float duration)
			{
				Undead = undead;
				TargetOverrider = targetOverrider;
				Duration = duration;
			}
		}

		[InjectEquipmentModifierParams]
		private Params _parameters;

		private GenericPooler<AttackHitParticleEffect> _spawnEffectPooler;

		private static Dictionary<int, GenericPooler<EnemyController>> _undeadPoolerMap;

		private static HashSet<BaseEnemyController> _undeadAIs;

		private static HashSet<BaseEnemyController> _markedEnemies;

		public override void Init(LinkedListNode<PlayerHandSlot> sourceSlotNode)
		{
			base.Init(sourceSlotNode);
			SceneMaster.Instance.OnSceneUnload -= ClearCache;
			SceneMaster.Instance.OnSceneUnload += ClearCache;
		}

		public override float GetRollChance()
		{
			return _parameters.chance;
		}

		public override float GetRollPriority()
		{
			return float.MaxValue;
		}

		public override OnKillModifierArgs ApplyEffect(OnKillModifierArgs args)
		{
			if (args.Enemy.ID == -1 || args.Enemy.ID == -2)
			{
				return args;
			}
			if (args.Enemy.isElite && !_parameters.affectElites)
			{
				return args;
			}
			if (_markedEnemies == null)
			{
				_markedEnemies = new HashSet<BaseEnemyController>();
			}
			if (_markedEnemies.Contains(args.Enemy))
			{
				return args;
			}
			EnemyController enemyController = args.Enemy as EnemyController;
			if (enemyController == null)
			{
				return args;
			}
			_markedEnemies.Add(args.Enemy);
			Vector2 position = args.Enemy.Transform.position;
			enemyController.OnDeathPresentationCompleted += Spawn;
			enemyController.OnDispose += UnMarkEnemy;
			return args;
			void Spawn()
			{
				SpawnUndead(_parameters.enemyPrefab, position).Forget();
			}
			void UnMarkEnemy()
			{
				_markedEnemies?.Remove(args.Enemy);
			}
		}

		private void RunSpawnEffect(Vector3 position)
		{
			if (_spawnEffectPooler == null)
			{
				_spawnEffectPooler = PoolManager.Instance.GetOrCreatePooler(_parameters.spawnEffectPrefab);
			}
			AttackHitParticleEffect particleEffect = _spawnEffectPooler.GetOrCreate(null, activate: true);
			particleEffect.transform.position = position;
			particleEffect.Play(ReturnToPool);
			void ReturnToPool()
			{
				_spawnEffectPooler.Return(particleEffect);
			}
		}

		private async UniTaskVoid SpawnUndead(EnemyController prefab, Vector2 position)
		{
			RunSpawnEffect(position);
			if (_undeadPoolerMap == null)
			{
				_undeadPoolerMap = new Dictionary<int, GenericPooler<EnemyController>>();
			}
			int instanceID = prefab.GetInstanceID();
			if (!_undeadPoolerMap.TryGetValue(instanceID, out var value))
			{
				value = PoolManager.Instance.GetOrCreatePooler(prefab);
				_undeadPoolerMap.Add(instanceID, value);
			}
			int variantIdx = _parameters.variantIdx;
			EnemyController newUndead = EnemyFactory.CreateEnemy(new EnemySpawnParams
			{
				Prefab = prefab,
				ID = -2,
				Pool = value,
				AttackTarget = null,
				VariantIdx = variantIdx,
				SpawnPosition = position + new Vector2(1000f, 1000f),
				SpeedMultiplierRange = Vector2.one,
				AllowRubberBand = false,
				RubberbandKillsEnemiesOnClipEnd = false,
				EndTime = 0f,
				ConfigureStatsBeforeCombatant = delegate(EnemyStats enemyStats)
				{
					enemyStats.BaseHealth = int.MaxValue;
					enemyStats.BaseXP = 0f;
					enemyStats.XP = 0f;
					enemyStats.XPMultiplier = 0f;
					enemyStats.BaseDamage = _parameters.damageValue;
					enemyStats.Damage = _parameters.damageValue;
				},
				OnConfirmedKill = null
			});
			newUndead.SetImmunity(state: true);
			SubscribeUndead(newUndead);
			await PlayAndWaitSpawnAnimation(newUndead, position);
			if (!(newUndead == null))
			{
				if (!newUndead.TryGetComponent<EnemyControllerTargetOverrider>(out var component))
				{
					component = newUndead.gameObject.AddComponent<EnemyControllerTargetOverrider>();
				}
				component.Init(newUndead, GetNewTarget);
				UndeadEntry undeadEntry = new UndeadEntry(newUndead, component, _parameters.lifeDuration);
				WaitAndDisposeUndead(undeadEntry).Forget();
			}
		}

		public async UniTask PlayAndWaitSpawnAnimation(EnemyController enemy, Vector2 position)
		{
			ClipTransition spawnAnimationClip = _parameters.GetSpawnAnimationClip(enemy.FacingDirection);
			UniTask animationTask = enemy.enemyAnimator.PlayOverridenAnimations(spawnAnimationClip, 1, resetOnEnd: true, blockOtherAnimations: true);
			await UniTask.DelayFrame(2);
			enemy.Transform.position = position;
			await animationTask;
		}

		private async UniTaskVoid WaitAndDisposeUndead(UndeadEntry undeadEntry)
		{
			await UniTask.Delay(TimeSpan.FromSeconds(undeadEntry.Duration));
			if ((bool)undeadEntry.Undead)
			{
				undeadEntry.TargetOverrider.Dispose();
				undeadEntry.Undead.Kill(instant: false, dropXp: false);
				UnSubscribeUndead(undeadEntry.Undead);
			}
		}

		private BaseEnemyController GetNewTarget(BaseEnemyController currentTarget, Vector3 currentPosition)
		{
			if (currentTarget != null && !currentTarget.IsDead)
			{
				return currentTarget;
			}
			return AIHelpers.FindClosestEnemyExclude(currentPosition, _undeadAIs);
		}

		private void ClearCache()
		{
			PlayerHand.Instance.OnReset -= ClearCache;
			_markedEnemies?.Clear();
			_spawnEffectPooler = null;
			_undeadPoolerMap?.Clear();
			_undeadAIs?.Clear();
		}

		private void SubscribeUndead(EnemyController undead)
		{
			if (_undeadAIs == null)
			{
				_undeadAIs = new HashSet<BaseEnemyController>();
			}
			_undeadAIs?.Add(undead);
		}

		private void UnSubscribeUndead(EnemyController undead)
		{
			_undeadAIs?.Remove(undead);
		}
	}
}
