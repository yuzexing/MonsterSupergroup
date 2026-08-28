using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.Pooling;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("On Hit Enemy Type Morph")]
	public class OnHitEnemyTypeMorphModifier : OnHitModifier
	{
		[EquipmentModifierParams]
		protected class Params : BaseParams
		{
			public bool affectElites;

			public AttackHitParticleEffect morphEffectPrefab;

			public EnemyController newEnemyPrefab;

			public int enemyVariantIdx;

			public float healthReductionMultiplier = 0.5f;
		}

		private GenericPooler<AttackHitParticleEffect> _morphEffectPooler;

		private GenericPooler<EnemyController> _newEnemyPool;

		private int _enemyID;

		[InjectEquipmentModifierParams]
		protected Params parameters;

		public int PrefabEnemyID
		{
			get
			{
				if (_enemyID == 0)
				{
					_enemyID = EnemyFactory.GenerateId(parameters.newEnemyPrefab.selectedName + parameters.enemyVariantIdx);
				}
				return _enemyID;
			}
		}

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
			return parameters.chance * (1f + parameters.healthReductionMultiplier);
		}

		protected override OnHitModifierArgs ApplyEffect(OnHitModifierArgs args)
		{
			if (args.Enemy.ID == PrefabEnemyID || args.Enemy.ID == -1)
			{
				return args;
			}
			if (args.Enemy.isElite && !parameters.affectElites)
			{
				return args;
			}
			if (!args.Enemy.IsAlive)
			{
				return args;
			}
			Vector2 vector = args.Enemy.Transform.position;
			SpawnMorphEffect(vector);
			DisposeEnemy(args.Enemy).Forget();
			args.Enemy = SpawnEnemy(parameters.newEnemyPrefab, args.Enemy, vector);
			SetImmunityFrames(args.Enemy, 1).Forget();
			return args;
		}

		private void SpawnMorphEffect(Vector3 position)
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

		private async UniTaskVoid DisposeEnemy(BaseEnemyController enemy)
		{
			Vector2 vector = enemy.Transform.position * new Vector2(500f, 500f);
			enemy.Transform.position = vector;
			await SetImmunityFrames(enemy, 2);
			if (enemy is EnemyController enemyController)
			{
				enemyController.Kill(instant: true, dropXp: false);
			}
		}

		private BaseEnemyController SpawnEnemy(EnemyController newEnemyPrefab, BaseEnemyController oldEnemy, Vector2 position)
		{
			if (_newEnemyPool == null)
			{
				_newEnemyPool = PoolManager.Instance.GetOrCreatePooler(newEnemyPrefab);
			}
			EnemyController enemyController = EnemyFactory.CreateEnemy(new EnemySpawnParams
			{
				Prefab = newEnemyPrefab,
				Pool = _newEnemyPool,
				AttackTarget = GameDirector.Instance.Player.EnemyAttackTarget,
				VariantIdx = parameters.enemyVariantIdx,
				SpawnPosition = position,
				SpeedMultiplierRange = Vector2.one,
				AllowRubberBand = true,
				RubberbandKillsEnemiesOnClipEnd = false,
				EndTime = 0f,
				ConfigureStatsBeforeCombatant = delegate(EnemyStats enemyStats)
				{
					enemyStats.BaseHealth = Mathf.CeilToInt(
						(float)oldEnemy.MaxHealth * parameters.healthReductionMultiplier);
					enemyStats.BaseXP = oldEnemy.stats.BaseXP;
					enemyStats.XP = oldEnemy.stats.XP;
					enemyStats.XPMultiplier = oldEnemy.stats.XPMultiplier;
				},
				OnConfirmedKill = null
			});
			oldEnemy.status.TransferTo(enemyController);
			return enemyController;
		}

		private async UniTask SetImmunityFrames(BaseEnemyController enemy, int frameCount)
		{
			enemy.SetImmunity(state: true);
			await UniTask.DelayFrame(frameCount);
			enemy.SetImmunity(state: false);
		}
	}
}
