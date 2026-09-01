using System;
using MonsterSupergroup.GAS;
using UnityEngine;
using CombatTags = MonsterSupergroup.GAS.CombatTags;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class ProjectileAttackBehaviour : WeaponBehaviour
	{
		protected override CombatTags DefaultCombatTags =>
			CombatTags.Attack | CombatTags.Projectile;

		[Header("Attack Settings")]
		[SerializeField]
		protected ProjectileAttackVariants variants;

		[SerializeField]
		protected float baseSpeed = 3f;

		[SerializeField]
		protected float spawnRadius = 0.5f;

		[SerializeField]
		protected int hitCount = 1;

		[SerializeField]
		protected bool rotateToMovement = true;

		[SerializeField]
		protected Vector3 positionOffset = Vector3.zero;

		private bool _presentationReplicaInitialized;

		public event Action<ProjectilePresentationSpawn> PresentationSpawned;

		public event Action<ProjectilePresentationTermination>
			PresentationTerminated;

		public override void Init(uint id, AttackStats stats)
		{
			base.Init(id, stats);
			variants.Init();
			LastAttackElapsedTime = GetCooldown() - Time.deltaTime;
		}

		protected void Update()
		{
			if (CheckCooldown())
			{
				Attack();
			}
			LastAttackElapsedTime += Time.deltaTime;
		}

		public override void Attack()
		{
			AttackSnapshot nativeAttack = null;
			if (UsesNativeGasRuntime)
			{
				nativeAttack = BeginNativeGasAttack();
			}
			else
			{
				base.Attack();
			}

			try
			{
				PlayAttackSound();
				int projectileCountValue = nativeAttack != null
					? nativeAttack.Stats.ProjectileCount
					: base.ProjectileCountValue;
				if (projectileCountValue == 1)
				{
					SpawnOwnedProjectile(
						nativeAttack,
						0,
						player.attackDirection.normalized);
					LastAttackElapsedTime = 0f;
					return;
				}
				for (int i = 0; i < projectileCountValue; i++)
				{
					Vector3 vector = Quaternion.AngleAxis(Vector2.SignedAngle(player.attackDirection, Vector2.right) + 360f / (float)projectileCountValue * (float)i, -Vector3.forward) * Vector3.right;
					SpawnOwnedProjectile(nativeAttack, i, vector.normalized);
				}
				LastAttackElapsedTime = 0f;
			}
			finally
			{
				nativeAttack?.Dispose();
			}
		}

		private void SpawnOwnedProjectile(
			AttackSnapshot nativeAttack,
			int projectileIndex,
			Vector2 direction)
		{
			AttackElement element = variants.ResolveElement(base.ActiveElement);
			ProjectileAttack attack = GetOrCreateAttack(nativeAttack, element);
			ProjectilePresentationKey key = default;
			if (nativeAttack != null)
			{
				if (projectileIndex < 0 || projectileIndex > ushort.MaxValue)
				{
					throw new InvalidOperationException(
						"Projectile index cannot be represented by the presentation protocol.");
				}

				key = new ProjectilePresentationKey(
					nativeAttack.Context.EventId.Value,
					(ushort)projectileIndex);
				attack.ConfigurePresentationLifecycle(
					key,
					HandleProjectileTermination);
			}

			attack.gameObject.SetActive(value: true);
			attack.transform.position = base.transform.position + positionOffset +
				(Vector3)direction.normalized * spawnRadius;
			attack.Attack(
				direction.normalized,
				baseSpeed,
				hitCount,
				rotateToMovement);

			if (nativeAttack != null)
			{
				PresentationSpawned?.Invoke(new ProjectilePresentationSpawn(
					base.ID,
					key,
					attack.transform.position,
					direction,
					element,
					rotateToMovement,
					ProjectilePresentationStats.From(
						nativeAttack.Stats,
						baseSpeed)));
			}
		}

		private void HandleProjectileTermination(
			ProjectilePresentationKey key,
			Vector3 position,
			ProjectilePresentationPhase phase)
		{
			PresentationTerminated?.Invoke(
				new ProjectilePresentationTermination(
					base.ID,
					key,
					position,
					phase));
		}

		protected ProjectileAttack GetOrCreateAttack(AttackSnapshot nativeAttack = null)
		{
			return GetOrCreateAttack(nativeAttack, base.ActiveElement);
		}

		private ProjectileAttack GetOrCreateAttack(
			AttackSnapshot nativeAttack,
			AttackElement element)
		{
			ProjectileAttack attack = variants.GetOrCreate(element, null);
			Action onEnd = delegate
			{
				attack.ReleaseNativeAttackSnapshot();
				variants.Return(attack);
			};
			if (nativeAttack != null)
			{
				attack.InitNative(this, nativeAttack, null, onEnd);
			}
			else
			{
				attack.Init(this, null, onEnd);
			}
			return attack;
		}

		public void InitializePresentationReplica(
			uint weaponId,
			PlayerMovement owner)
		{
			if (_presentationReplicaInitialized)
			{
				return;
			}

			ConfigureOwner(owner);
			_id = weaponId;
			variants.Init();
			_presentationReplicaInitialized = true;
			enabled = false;
		}

		public ProjectileAttack PlayPresentation(
			ProjectilePresentationSpawn spawn,
			float elapsedSeconds,
			Action<ProjectileAttack> onReturned = null)
		{
			if (!_presentationReplicaInitialized || spawn.WeaponId != base.ID)
			{
				throw new InvalidOperationException(
					"Projectile presentation replica is not initialized for this weapon.");
			}

			ProjectileAttack attack = variants.GetOrCreate(spawn.Element, null);
			Action onEnd = delegate
			{
				variants.Return(attack);
				onReturned?.Invoke(attack);
			};
			attack.InitPresentation(this, spawn.Stats, null, onEnd);
			attack.gameObject.SetActive(value: true);
			attack.transform.position = spawn.Position;
			attack.PlayPresentation(spawn, elapsedSeconds);
			return attack;
		}

		public void DisposePresentationReplica()
		{
			if (!_presentationReplicaInitialized)
			{
				return;
			}

			variants.Dispose(attack => attack.ReleaseNativeAttackSnapshot());
			_presentationReplicaInitialized = false;
		}

		protected override void Dispose()
		{
			variants.Dispose(attack => attack.ReleaseNativeAttackSnapshot());
			LastAttackElapsedTime = 0f;
		}
	}
}
