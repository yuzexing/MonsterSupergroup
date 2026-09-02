using System;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using UnityEngine;
using GasAttackStats = MonsterSupergroup.GAS.AttackStats;

namespace AstralShift.HellMaiden.Data.Cards
{
	[CreateAssetMenu(fileName = "New Weapon Data", menuName = "HellMaiden/Data/Cards/Weapon Data")]
	public class WeaponData : CardData
	{
		[Header("Weapon Settings")]
		public WeaponBehaviour WeaponPrefab;

		[Space]
		[SerializeField]
		protected bool isSignature;

		[Space]
		[SerializeField]
		protected UltimateData ultimateData;

		[SerializeField]
		protected GasAttackStats baseStats;

		[SerializeField]
		private long attackTagsValue = (long)CombatTags.Attack;

		[SerializeField]
		private WeaponPresentationSettings presentation = new WeaponPresentationSettings();

		[SerializeField]
		protected WeaponRarity rarity;

		public ModifierFlags modifierFlags;

		public bool IsSignature => isSignature;

		public UltimateData UltimateData => ultimateData;

		public GasAttackStats BaseStats => baseStats;

		public CombatTags AttackTags =>
			(CombatTags)(ulong)attackTagsValue | CombatTags.Attack;

		public WeaponPresentationSettings Presentation => presentation;

		public WeaponRarity Rarity => rarity;

		public bool Supports(MonsterSupergroup.GAS.EquipmentModifierID modifierId)
		{
			ModifierFlags requiredFlag;
			switch (modifierId.Value)
			{
			case DamageStatModifier.ModifierIdValue:
				requiredFlag = ModifierFlags.Damage;
				break;
			case SpeedStatModifier.ModifierIdValue:
				requiredFlag = ModifierFlags.Speed;
				break;
			case SizeStatModifier.ModifierIdValue:
				requiredFlag = ModifierFlags.Size;
				break;
			case DurationStatModifier.ModifierIdValue:
				requiredFlag = ModifierFlags.Duration;
				break;
			case CritRateStatModifier.ModifierIdValue:
				requiredFlag = ModifierFlags.CritRate;
				break;
			case CritMultiplierStatModifier.ModifierIdValue:
				requiredFlag = ModifierFlags.CritDamage;
				break;
			case ProjectileCountStatModifier.ModifierIdValue:
				requiredFlag = ModifierFlags.ProjectileCount;
				break;
			case KnockbackStatModifier.ModifierIdValue:
				requiredFlag = ModifierFlags.KnockBack;
				break;
			default:
				return true;
			}

			return (modifierFlags & requiredFlag) != 0;
		}

		public void ConfigureNativeGas(
			GasAttackStats newBaseStats,
			CombatTags newAttackTags,
			WeaponPresentationSettings newPresentation)
		{
			baseStats = newBaseStats;
			CombatTags normalizedTags = newAttackTags | CombatTags.Attack;
			if (((ulong)normalizedTags & 0x8000000000000000UL) != 0UL)
			{
				throw new ArgumentOutOfRangeException(
					nameof(newAttackTags),
					"Serialized combat tags must fit in a signed 64-bit value.");
			}

			attackTagsValue = (long)(ulong)normalizedTags;
			presentation = newPresentation ?? throw new ArgumentNullException(nameof(newPresentation));
			ValidateNativeGas();
		}

		public void ValidateNativeGas()
		{
			if (ID == 0u)
			{
				throw new InvalidOperationException($"{name} has a zero weapon ID.");
			}

			if (baseStats.damage < 0 || baseStats.projectileCount < 1 ||
				!FiniteNonNegative(baseStats.critMultiplier) ||
				!FiniteNonNegative(baseStats.critRate) ||
				!FinitePositive(baseStats.speed) ||
				!FiniteNonNegative(baseStats.size) ||
				!FiniteNonNegative(baseStats.duration) ||
				!FiniteNonNegative(baseStats.knockbackDistance))
			{
				throw new InvalidOperationException($"{name} contains invalid base attack stats.");
			}

			if (presentation == null)
			{
				throw new InvalidOperationException($"{name} has no presentation settings.");
			}
		}

		public float GetBaseDamage()
		{
			return BaseStats.damage;
		}

		public float GetBaseSize()
		{
			return BaseStats.size;
		}

		public float GetBaseDuration()
		{
			return BaseStats.duration;
		}

		public float GetBaseSpeed()
		{
			return WeaponPrefab.GetAttacksPerSecond(BaseStats.speed);
		}

		public float GetBaseCritMultiplier()
		{
			return BaseStats.critMultiplier;
		}

		public float GetBaseCritRate()
		{
			return BaseStats.critRate;
		}

		public int GetBaseProjectileCount()
		{
			return BaseStats.projectileCount;
		}

		public float GetBaseKnockbackDistance()
		{
			return BaseStats.knockbackDistance;
		}

		private static bool FinitePositive(float value) =>
			value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

		private static bool FiniteNonNegative(float value) =>
			value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
