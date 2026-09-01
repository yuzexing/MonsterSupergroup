using System;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public enum ProjectilePresentationPhase : byte
	{
		Spawn = 0,
		Hit = 1,
		Expired = 2,
		Cancelled = 3
	}

	[Serializable]
	public struct ProjectilePresentationKey : IEquatable<ProjectilePresentationKey>
	{
		public ulong AttackEventId;
		public ushort ProjectileIndex;

		public ProjectilePresentationKey(
			ulong attackEventId,
			ushort projectileIndex)
		{
			AttackEventId = attackEventId;
			ProjectileIndex = projectileIndex;
		}

		public bool IsValid => AttackEventId != 0UL;

		public bool Equals(ProjectilePresentationKey other)
		{
			return AttackEventId == other.AttackEventId &&
				ProjectileIndex == other.ProjectileIndex;
		}

		public override bool Equals(object obj)
		{
			return obj is ProjectilePresentationKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				return (AttackEventId.GetHashCode() * 397) ^ ProjectileIndex;
			}
		}
	}

	[Serializable]
	public struct ProjectilePresentationStats
	{
		public float DamageMultiplierSum;
		public float SpeedMultiplierSum;
		public float SizeMultiplierSum;
		public float DurationMultiplierSum;
		public float EffectiveSpeed;
		public float Duration;
		public int ProjectileCount;
		public int BaseProjectileCount;

		public static ProjectilePresentationStats From(
			AttackStatsSnapshot snapshot,
			float baseMovementSpeed)
		{
			return new ProjectilePresentationStats
			{
				DamageMultiplierSum = snapshot.DamageMultiplierSum,
				SpeedMultiplierSum = snapshot.SpeedMultiplierSum,
				SizeMultiplierSum = snapshot.SizeMultiplierSum,
				DurationMultiplierSum = snapshot.DurationMultiplierSum,
				EffectiveSpeed =
					baseMovementSpeed * snapshot.SpeedMultipliersProduct,
				Duration = snapshot.Duration,
				ProjectileCount = snapshot.ProjectileCount,
				BaseProjectileCount = snapshot.BaseProjectileCount
			};
		}

		public bool IsFinite =>
			IsFiniteValue(DamageMultiplierSum) &&
			IsFiniteValue(SpeedMultiplierSum) &&
			IsFiniteValue(SizeMultiplierSum) &&
			IsFiniteValue(DurationMultiplierSum) &&
			IsFiniteValue(EffectiveSpeed) && EffectiveSpeed >= 0f &&
			IsFiniteValue(Duration) && Duration >= 0f &&
			ProjectileCount > 0 && BaseProjectileCount > 0;

		private static bool IsFiniteValue(float value)
		{
			return !float.IsNaN(value) && !float.IsInfinity(value);
		}
	}

	public readonly struct ProjectilePresentationSpawn
	{
		public ProjectilePresentationSpawn(
			uint weaponId,
			ProjectilePresentationKey key,
			Vector3 position,
			Vector2 direction,
			AttackElement element,
			bool rotateToMovement,
			ProjectilePresentationStats stats)
		{
			WeaponId = weaponId;
			Key = key;
			Position = position;
			Direction = direction.normalized;
			Element = element;
			RotateToMovement = rotateToMovement;
			Stats = stats;
		}

		public uint WeaponId { get; }
		public ProjectilePresentationKey Key { get; }
		public Vector3 Position { get; }
		public Vector2 Direction { get; }
		public AttackElement Element { get; }
		public bool RotateToMovement { get; }
		public ProjectilePresentationStats Stats { get; }
	}

	public readonly struct ProjectilePresentationTermination
	{
		public ProjectilePresentationTermination(
			uint weaponId,
			ProjectilePresentationKey key,
			Vector3 position,
			ProjectilePresentationPhase phase)
		{
			if (phase == ProjectilePresentationPhase.Spawn)
			{
				throw new ArgumentException(
					"A projectile termination cannot use the Spawn phase.",
					nameof(phase));
			}

			WeaponId = weaponId;
			Key = key;
			Position = position;
			Phase = phase;
		}

		public uint WeaponId { get; }
		public ProjectilePresentationKey Key { get; }
		public Vector3 Position { get; }
		public ProjectilePresentationPhase Phase { get; }
	}
}
