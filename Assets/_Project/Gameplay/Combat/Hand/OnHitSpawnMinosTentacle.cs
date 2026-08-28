using System;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.Pooling;
using Cysharp.Threading.Tasks;
using UnityEngine;
using CombatTags = MonsterSupergroup.GAS.CombatTags;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("On Hit Spawn Tentacle")]
	public class OnHitSpawnMinosTentacle : OnHitModifier
	{
		[EquipmentModifierParams]
		protected class Params : BaseParams
		{
			public float damageMultiplier;

			public float sizeMultiplier;

			public AnimatedAttack tailPrefab;

			public int minAttackCount = 3;

			public int maxAttackCount = 3;

			public int minIdleBetweenAttacks = 1;

			public int maxIdleBetweenAttacks = 1;

			public float minSizeMultiplier = 1f;

			public float maxSizeMultiplier = 1f;
		}

		[InjectEquipmentModifierParams]
		protected Params parameters;

		private GenericPooler<AnimatedAttack> _tailPooler;

		public override float GetRollChance()
		{
			return parameters.chance;
		}

		public override float GetRollPriority()
		{
			return parameters.damageMultiplier * parameters.sizeMultiplier;
		}

		protected override OnHitModifierArgs ApplyEffect(OnHitModifierArgs args)
		{
			if (args.Enemy == null || args.Enemy.stats == null)
			{
				return args;
			}
			Spawn(args);
			return args;
		}

		private void Spawn(OnHitModifierArgs args)
		{
			Vector2 position = args.Enemy.transform.position;
			SpawnTail(position, args).Forget();
		}

		private async UniTaskVoid SpawnTail(Vector2 position, OnHitModifierArgs args)
		{
			if (_tailPooler == null)
			{
				_tailPooler = PoolManager.Instance.GetOrCreatePooler(parameters.tailPrefab);
			}
			AnimatedAttack tailAttackEffect = _tailPooler.GetOrCreate(null, activate: true);
			tailAttackEffect.transform.position = position;
			tailAttackEffect.animancer.Stop();
			float num = UnityEngine.Random.Range(parameters.minSizeMultiplier, parameters.maxSizeMultiplier);
			tailAttackEffect.transform.GetChild(0).localScale = Vector3.one * num;
			Vector2 normalized = (position - (Vector2)GameDirector.Instance.Player.transform.position).normalized;
			tailAttackEffect.Init(args.Weapon, null, delegate
			{
				ReturnToPool(tailAttackEffect);
			});
			tailAttackEffect.hitbox.Init(delegate(IDamageable idmg)
			{
				LegacyDamageDispatcher.Damage(
					idmg,
					Mathf.CeilToInt((float)args.Weapon.DamageValue * parameters.damageMultiplier),
					DamageType.Normal,
					args.Source,
					CombatTags.Build);
			});
			int attackCount = UnityEngine.Random.Range(parameters.minAttackCount, parameters.maxAttackCount + 1);
			int idleBetweenAttacks = UnityEngine.Random.Range(parameters.minIdleBetweenAttacks, parameters.maxIdleBetweenAttacks + 1);
			await SequentialAttacks(tailAttackEffect, normalized, attackCount, idleBetweenAttacks);
		}

		private async UniTask SequentialAttacks(AnimatedAttack tail, Vector2 direction, int attackCount, int idleBetweenAttacks)
		{
			tail.Attack(direction, rotateToDirection: false);
			await UniTask.Delay(TimeSpan.FromSeconds(tail.attackStartAnim.Clip.length));
			float idleAnimationSpeedValue = UnityEngine.Random.Range(1.5f, 2f);
			for (int i = 0; i < attackCount; i++)
			{
				for (int idleCount = 0; idleCount < idleBetweenAttacks; idleCount++)
				{
					tail.PlayHitAnimation();
					await UniTask.Delay(TimeSpan.FromSeconds(tail.attackHitAnim.Clip.length / idleAnimationSpeedValue));
				}
				tail.PlayAttackAnimation();
				await UniTask.Delay(TimeSpan.FromSeconds(tail.attackAnim.Clip.length));
			}
			tail.PlayEndAnimation();
		}

		private void ReturnToPool(AnimatedAttack tailAttackEffect)
		{
			if (_tailPooler != null && tailAttackEffect != null)
			{
				_tailPooler.Return(tailAttackEffect);
			}
		}
	}
}
