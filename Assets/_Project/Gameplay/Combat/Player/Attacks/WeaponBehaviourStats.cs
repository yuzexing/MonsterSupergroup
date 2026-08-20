using System;
using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	[Serializable]
	public class WeaponBehaviourStats
	{
		public enum StatFormulaMultipliers
		{
			Base = 0,
			BaseAndDynamic = 1,
			BaseAndPlayer = 2,
			All = 3
		}

		[SerializeField]
		private AttackStats baseStats;

		[SerializeField]
		private AttackStatsMultipliers baseStatsMultipliers;

		[SerializeField]
		private AttackStatsMultipliers dynamicStatsMultipliers;

		[SerializeField]
		private PlayerStats playerStats;

		private Dictionary<AttackStatType, AttackStatType> _statsMap;

		private bool _isInitialized;

		private bool _isResolvingRemapping;

		public AttackStats BaseStats => baseStats;

		public AttackStatsMultipliers BaseStatsMultipliers => baseStatsMultipliers;

		public AttackStatsMultipliers DynamicStatsMultipliers => dynamicStatsMultipliers;

		public PlayerStats PlayerStats => playerStats;

		public int DamageValue => Mathf.CeilToInt(GetStatValue(AttackStatType.Damage));

		public float DamageMultiplierSum => GetRemappedMultiplierSum(AttackStatType.Damage);

		public float SizeValue => GetStatValue(AttackStatType.Size);

		public float SizeMultiplierSum => GetRemappedMultiplierSum(AttackStatType.Size);

		public float SpeedValue => GetStatValue(AttackStatType.Speed);

		public float SpeedMultiplierSum => GetRemappedMultiplierSum(AttackStatType.Speed);

		public float SpeedMultipliersProduct => GetRemappedMultiplierProduct(AttackStatType.Speed);

		public float DurationValue => GetStatValue(AttackStatType.Duration);

		public float DurationMultiplierSum => GetRemappedMultiplierSum(AttackStatType.Duration);

		public int ProjectileCountValue => (int)GetStatValue(AttackStatType.ProjectileCount);

		public float CritRate => GetStatValue(AttackStatType.CritRate);

		public float CritRateMultiplierSum => GetRemappedMultiplierSum(AttackStatType.CritRate);

		public float CritDamageMultiplier => GetStatValue(AttackStatType.CritDamage);

		public float CritDamageMultiplierSum => GetRemappedMultiplierSum(AttackStatType.CritDamage);

		public int CriticalDamageValue => (int)((float)DamageValue * CritDamageMultiplier);

		public float KnockBackDistance => GetStatValue(AttackStatType.Knockback);

		public float KnockBackMultiplierSum => GetRemappedMultiplierSum(AttackStatType.Knockback);

		public WeaponBehaviourStats(AttackStats baseStats, PlayerStats playerStats)
		{
			this.baseStats = baseStats;
			this.playerStats = playerStats;
			baseStatsMultipliers = new AttackStatsMultipliers();
			dynamicStatsMultipliers = new AttackStatsMultipliers();
			_statsMap = new Dictionary<AttackStatType, AttackStatType>
			{
				{
					AttackStatType.Damage,
					AttackStatType.Damage
				},
				{
					AttackStatType.Size,
					AttackStatType.Size
				},
				{
					AttackStatType.Speed,
					AttackStatType.Speed
				},
				{
					AttackStatType.Duration,
					AttackStatType.Duration
				},
				{
					AttackStatType.ProjectileCount,
					AttackStatType.ProjectileCount
				},
				{
					AttackStatType.CritRate,
					AttackStatType.CritRate
				},
				{
					AttackStatType.CritDamage,
					AttackStatType.CritDamage
				},
				{
					AttackStatType.Knockback,
					AttackStatType.Knockback
				}
			};
			_isInitialized = true;
		}

		public float GetStatValue(AttackStatType target, StatFormulaMultipliers formulaMultipliers = StatFormulaMultipliers.All)
		{
			if (_statsMap == null)
			{
				return 0f;
			}
			AttackStatType type = _statsMap[target];
			float baseStatValue = GetBaseStatValue(target);
			if (target == AttackStatType.CritRate || target == AttackStatType.CritDamage || target == AttackStatType.ProjectileCount)
			{
				float value = baseStatValue;
				switch (formulaMultipliers)
				{
				case StatFormulaMultipliers.Base:
					value = baseStatValue + GetMultiplierFromType(type, baseStatsMultipliers);
					break;
				case StatFormulaMultipliers.BaseAndDynamic:
					value = baseStatValue + GetMultiplierFromType(type, baseStatsMultipliers) + GetMultiplierFromType(type, dynamicStatsMultipliers);
					break;
				case StatFormulaMultipliers.BaseAndPlayer:
					value = baseStatValue + GetMultiplierFromType(type, baseStatsMultipliers) + GetMultiplierFromType(target, playerStats.StatMultipliers.attackStatsMultipliers);
					break;
				case StatFormulaMultipliers.All:
					value = baseStatValue + GetMultiplierFromType(type, baseStatsMultipliers) + GetMultiplierFromType(type, dynamicStatsMultipliers) + GetMultiplierFromType(target, playerStats.StatMultipliers.attackStatsMultipliers);
					break;
				}
				return Mathf.Clamp(value, (target == AttackStatType.ProjectileCount) ? 1 : 0, float.MaxValue);
			}
			return formulaMultipliers switch
			{
				StatFormulaMultipliers.Base => baseStatValue * CalculateMultiplierFormula(GetMultiplierFromType(type, baseStatsMultipliers)), 
				StatFormulaMultipliers.BaseAndDynamic => baseStatValue * CalculateMultiplierFormula(GetMultiplierFromType(type, baseStatsMultipliers)) * CalculateMultiplierFormula(GetMultiplierFromType(type, dynamicStatsMultipliers)), 
				StatFormulaMultipliers.BaseAndPlayer => baseStatValue * CalculateMultiplierFormula(GetMultiplierFromType(type, baseStatsMultipliers)) * CalculateMultiplierFormula(GetMultiplierFromType(target, playerStats.StatMultipliers.attackStatsMultipliers)), 
				StatFormulaMultipliers.All => baseStatValue * CalculateMultiplierFormula(GetMultiplierFromType(type, baseStatsMultipliers)) * CalculateMultiplierFormula(GetMultiplierFromType(type, dynamicStatsMultipliers)) * CalculateMultiplierFormula(GetMultiplierFromType(target, playerStats.StatMultipliers.attackStatsMultipliers)), 
				_ => baseStatValue * CalculateMultiplierFormula(GetMultiplierFromType(type, baseStatsMultipliers)) * CalculateMultiplierFormula(GetMultiplierFromType(type, dynamicStatsMultipliers)) * CalculateMultiplierFormula(GetMultiplierFromType(target, playerStats.StatMultipliers.attackStatsMultipliers)), 
			};
		}

		private float GetBaseStatValue(AttackStatType type)
		{
			return type switch
			{
				AttackStatType.Damage => baseStats.damage, 
				AttackStatType.Size => baseStats.size, 
				AttackStatType.Speed => baseStats.speed, 
				AttackStatType.Duration => baseStats.duration, 
				AttackStatType.ProjectileCount => baseStats.projectileCount, 
				AttackStatType.CritRate => baseStats.critRate, 
				AttackStatType.CritDamage => baseStats.critMultiplier, 
				AttackStatType.Knockback => baseStats.knockbackSettings.distance, 
				_ => 0f, 
			};
		}

		public void RemapStat(AttackStatType target, AttackStatType source)
		{
			_statsMap[target] = source;
		}

		public void ResetStatRemaps()
		{
			_statsMap[AttackStatType.Damage] = AttackStatType.Damage;
			_statsMap[AttackStatType.Size] = AttackStatType.Size;
			_statsMap[AttackStatType.Speed] = AttackStatType.Speed;
			_statsMap[AttackStatType.Duration] = AttackStatType.Duration;
			_statsMap[AttackStatType.ProjectileCount] = AttackStatType.ProjectileCount;
			_statsMap[AttackStatType.CritRate] = AttackStatType.CritRate;
			_statsMap[AttackStatType.CritDamage] = AttackStatType.CritDamage;
			_statsMap[AttackStatType.Knockback] = AttackStatType.Knockback;
		}

		private float GetRemappedValue(AttackStatType target)
		{
			if (_isResolvingRemapping)
			{
				Debug.LogWarning($"WeaponBehaviourStats: Recursive remapping detected for {target}.");
				return 0f;
			}
			AttackStatType attackStatType = _statsMap[target];
			_isResolvingRemapping = true;
			float result = 0f;
			switch (attackStatType)
			{
			case AttackStatType.Damage:
				result = DamageValue;
				break;
			case AttackStatType.Size:
				result = SizeValue;
				break;
			case AttackStatType.Speed:
				result = SpeedValue;
				break;
			case AttackStatType.Duration:
				result = DurationValue;
				break;
			case AttackStatType.ProjectileCount:
				result = ProjectileCountValue;
				break;
			case AttackStatType.CritRate:
				result = CritRate;
				break;
			case AttackStatType.CritDamage:
				result = CritDamageMultiplier;
				break;
			case AttackStatType.Knockback:
				result = KnockBackDistance;
				break;
			}
			_isResolvingRemapping = false;
			return result;
		}

		private float GetRemappedMultiplierSum(AttackStatType target, StatFormulaMultipliers formulaMultipliers = StatFormulaMultipliers.All)
		{
			if (_statsMap == null)
			{
				return 0f;
			}
			AttackStatType type = _statsMap[target];
			return formulaMultipliers switch
			{
				StatFormulaMultipliers.Base => GetMultiplierFromType(type, baseStatsMultipliers), 
				StatFormulaMultipliers.BaseAndDynamic => GetMultiplierFromType(type, baseStatsMultipliers) + GetMultiplierFromType(type, dynamicStatsMultipliers), 
				StatFormulaMultipliers.BaseAndPlayer => GetMultiplierFromType(type, baseStatsMultipliers) + GetMultiplierFromType(type, playerStats.StatMultipliers.attackStatsMultipliers), 
				StatFormulaMultipliers.All => GetMultiplierFromType(type, baseStatsMultipliers) + GetMultiplierFromType(type, dynamicStatsMultipliers) + GetMultiplierFromType(type, playerStats.StatMultipliers.attackStatsMultipliers), 
				_ => GetMultiplierFromType(type, baseStatsMultipliers) + GetMultiplierFromType(type, dynamicStatsMultipliers) + GetMultiplierFromType(type, playerStats.StatMultipliers.attackStatsMultipliers), 
			};
		}

		private float GetRemappedMultiplierProduct(AttackStatType target, StatFormulaMultipliers formulaMultipliers = StatFormulaMultipliers.All)
		{
			if (_statsMap == null)
			{
				return 0f;
			}
			AttackStatType type = _statsMap[target];
			return formulaMultipliers switch
			{
				StatFormulaMultipliers.Base => CalculateMultiplierFormula(GetMultiplierFromType(type, baseStatsMultipliers)), 
				StatFormulaMultipliers.BaseAndDynamic => CalculateMultiplierFormula(GetMultiplierFromType(type, baseStatsMultipliers)) * CalculateMultiplierFormula(GetMultiplierFromType(type, dynamicStatsMultipliers)), 
				StatFormulaMultipliers.BaseAndPlayer => CalculateMultiplierFormula(GetMultiplierFromType(type, baseStatsMultipliers)) * CalculateMultiplierFormula(GetMultiplierFromType(type, playerStats.StatMultipliers.attackStatsMultipliers)), 
				StatFormulaMultipliers.All => CalculateMultiplierFormula(GetMultiplierFromType(type, baseStatsMultipliers)) * CalculateMultiplierFormula(GetMultiplierFromType(type, dynamicStatsMultipliers)) * CalculateMultiplierFormula(GetMultiplierFromType(type, playerStats.StatMultipliers.attackStatsMultipliers)), 
				_ => CalculateMultiplierFormula(GetMultiplierFromType(type, baseStatsMultipliers)) * CalculateMultiplierFormula(GetMultiplierFromType(type, dynamicStatsMultipliers)) * CalculateMultiplierFormula(GetMultiplierFromType(type, playerStats.StatMultipliers.attackStatsMultipliers)), 
			};
		}

		public float GetMultiplierFromType(AttackStatType type, AttackStatsMultipliers multipliers)
		{
			return type switch
			{
				AttackStatType.Damage => multipliers.damage, 
				AttackStatType.Size => multipliers.size, 
				AttackStatType.Speed => multipliers.speed, 
				AttackStatType.Duration => multipliers.duration, 
				AttackStatType.ProjectileCount => multipliers.projectileCountIncrement, 
				AttackStatType.CritRate => multipliers.critRate, 
				AttackStatType.CritDamage => multipliers.critDamage, 
				AttackStatType.Knockback => multipliers.knockBackMultiplier, 
				_ => 0f, 
			};
		}

		public float CalculateMultiplierFormula(float multiplier)
		{
			if (multiplier >= 0f)
			{
				return 1f + multiplier;
			}
			return 1f / (1f + MathF.Abs(multiplier));
		}
	}
}
