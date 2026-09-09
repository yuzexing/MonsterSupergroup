using System;
using System.Collections.Generic;
using Assets.Scripts.AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.Player
{
	[Serializable]
	public class PlayerStats
	{
		[Serializable]
		public struct PlayerStatsValues
		{
			public int HP;

			public int maxHP;

			public float moveSpeed;

			public float dashDistance;

			public float dashSpeed;

			public float dashCooldown;

			public int maxDashCharges;

			public int dashCharges;

			public float pullArea;

			public float xpModifier;

			public float dmgReduction;

			public int cardsReRollsAmount;

			public int cardBanishesAmount;

			public int perksRerollsAmount;

			public int perkBanishesAmount;

			public int reviveAmount;
		}

		[Serializable]
		public class PlayerStatsMultipliers
		{
			public float HPMultiplier;

			public float moveSpeedMultiplier;

			public float dashDistanceMultiplier;

			public float dashSpeedMultiplier;

			public float dashCooldownMultiplier;

			public float xpPullRadiusMultiplier;

			public float xpAmountMultiplier;

			public float receivedDamageMultiplier;

			public float currencyMultiplier;

			public int extraDashCharges;

			public int reviveChancesAmountReceiver;

			public AttackStatsMultipliers baseAttackStatsMultipliers;

			public AttackStatsMultipliers attackStatsMultipliers;

			public void Reset()
			{
				HPMultiplier = 0f;
				moveSpeedMultiplier = 0f;
				dashDistanceMultiplier = 0f;
				dashSpeedMultiplier = 0f;
				dashCooldownMultiplier = 0f;
				xpPullRadiusMultiplier = 0f;
				xpAmountMultiplier = 0f;
				receivedDamageMultiplier = 0f;
				extraDashCharges = 0;
				attackStatsMultipliers.Reset();
				attackStatsMultipliers.damage = baseAttackStatsMultipliers.damage;
				attackStatsMultipliers.critRate = baseAttackStatsMultipliers.critRate;
				attackStatsMultipliers.critDamage = baseAttackStatsMultipliers.critDamage;
				attackStatsMultipliers.speed = baseAttackStatsMultipliers.speed;
				attackStatsMultipliers.size = baseAttackStatsMultipliers.size;
				attackStatsMultipliers.duration = baseAttackStatsMultipliers.duration;
				attackStatsMultipliers.projectileCountIncrement = baseAttackStatsMultipliers.projectileCountIncrement;
			}
		}

		[Serializable]
		public class EquipmentStatsMultipliers
		{
			public float OnHitChanceMultiplier = 1f;

			public float OnKillChanceMultiplier = 1f;

			public void Reset()
			{
				OnHitChanceMultiplier = 1f;
				OnKillChanceMultiplier = 1f;
			}
		}

		public PlayerStatsValues baseStats;

		public PlayerStatsValues currentStats;

		public PlayerStatsMultipliers statMultipliers;

		public EquipmentStatsMultipliers equipmentStatsMultipliers;

		private List<PlayerPerkModifier> _playerPerkModifiers;

		private List<WeaponStatsPerkModifier> _weaponPerkModifiers;

		private List<EnemyConditionPerkModifier> _onEnemyDamagePerkModifiers;

		private List<EquipmentPerkModifier> _equipmentPerkModifiers;

		public PlayerBaseStatsDatabase playerBaseStatsDatabase;

		public int MaxHP => currentStats.maxHP;

		public PlayerStatsMultipliers StatMultipliers => statMultipliers;

		public event Action<int> MaximumHealthChanged;

		public void Init()
		{
			if (playerBaseStatsDatabase == null)
				throw new InvalidOperationException("PlayerStats requires a base stats definition.");
			Initialize(playerBaseStatsDatabase.values);
		}

		/// <summary>Initializes this player's accepted values without reading an account save.</summary>
		public void Initialize(PlayerStatsValues values)
		{
			if (values.maxHP < 1)
				throw new ArgumentOutOfRangeException(nameof(values), "Player MaxHP must be positive.");
			baseStats = values;
			baseStats.HP = baseStats.maxHP;
			baseStats.maxDashCharges = baseStats.dashCharges;
			statMultipliers ??= new PlayerStatsMultipliers();
			statMultipliers.baseAttackStatsMultipliers ??= new AttackStatsMultipliers();
			statMultipliers.attackStatsMultipliers ??= new AttackStatsMultipliers();
			equipmentStatsMultipliers ??= new EquipmentStatsMultipliers();
			statMultipliers.baseAttackStatsMultipliers.Reset();
			currentStats = baseStats;
			if (_playerPerkModifiers == null)
			{
				_playerPerkModifiers = new List<PlayerPerkModifier>();
			}
			if (_weaponPerkModifiers == null)
			{
				_weaponPerkModifiers = new List<WeaponStatsPerkModifier>();
			}
			if (_onEnemyDamagePerkModifiers == null)
			{
				_onEnemyDamagePerkModifiers = new List<EnemyConditionPerkModifier>();
			}
			if (_equipmentPerkModifiers == null)
			{
				_equipmentPerkModifiers = new List<EquipmentPerkModifier>();
			}
			RemoveAllModifiers();
		}

		public void AddModifier(RuntimePerkModifier modifier)
		{
			if (!(modifier is PlayerPerkModifier item))
			{
				if (!(modifier is WeaponStatsPerkModifier item2))
				{
					if (!(modifier is EnemyConditionPerkModifier item3))
					{
						if (modifier is EquipmentPerkModifier item4)
						{
							_equipmentPerkModifiers.Add(item4);
						}
					}
					else
					{
						_onEnemyDamagePerkModifiers.Add(item3);
					}
				}
				else
				{
					_weaponPerkModifiers.Add(item2);
				}
			}
			else
			{
				_playerPerkModifiers.Add(item);
			}
			EvaluateModifiers();
		}

		public void RemoveModifier(RuntimePerkModifier modifier)
		{
			if (!(modifier is PlayerPerkModifier item))
			{
				if (!(modifier is WeaponStatsPerkModifier item2))
				{
					if (!(modifier is EnemyConditionPerkModifier item3))
					{
						if (modifier is EquipmentPerkModifier item4)
						{
							_equipmentPerkModifiers.Remove(item4);
						}
					}
					else
					{
						_onEnemyDamagePerkModifiers.Remove(item3);
					}
				}
				else
				{
					_weaponPerkModifiers.Remove(item2);
				}
			}
			else
			{
				_playerPerkModifiers.Remove(item);
			}
			EvaluateModifiers();
		}

		public void RemoveAllModifiers()
		{
			_playerPerkModifiers.Clear();
			_weaponPerkModifiers.Clear();
			_onEnemyDamagePerkModifiers.Clear();
			_equipmentPerkModifiers.Clear();
			EvaluateModifiers();
		}

		public void EvaluateModifiers()
		{
			StatMultipliers.Reset();
			equipmentStatsMultipliers.Reset();
			EvaluatePlayerPerkModifiers();
			UpdateMaxDashes();
			UpdateMaxHealth();
			EvaluateWeaponPerkModifiers();
			EvaluateEquipmentPerkModifiers();
			EvaluateOnEnemyDamagePerkModifiers();
		}

		private void EvaluatePlayerPerkModifiers()
		{
			if (_playerPerkModifiers == null)
			{
				return;
			}
			for (int i = 0; i < _playerPerkModifiers.Count; i++)
			{
				if (_playerPerkModifiers[i] != null)
				{
					_playerPerkModifiers[i].Apply(StatMultipliers);
				}
			}
			currentStats.moveSpeed = baseStats.moveSpeed * (1f + StatMultipliers.moveSpeedMultiplier);
			currentStats.dashDistance = baseStats.dashDistance * (1f + StatMultipliers.dashDistanceMultiplier);
			currentStats.dashSpeed = baseStats.dashSpeed * (1f + StatMultipliers.dashSpeedMultiplier);
			currentStats.dashCooldown = baseStats.dashCooldown * (1f + StatMultipliers.dashCooldownMultiplier);
			currentStats.pullArea = baseStats.pullArea * (1f + StatMultipliers.xpPullRadiusMultiplier);
			currentStats.xpModifier = baseStats.xpModifier * (1f + StatMultipliers.xpAmountMultiplier);
			currentStats.dmgReduction = baseStats.dmgReduction + StatMultipliers.receivedDamageMultiplier;
			currentStats.reviveAmount = baseStats.reviveAmount + StatMultipliers.reviveChancesAmountReceiver;
		}

		private void EvaluateWeaponPerkModifiers()
		{
			if (_weaponPerkModifiers == null)
			{
				return;
			}
			for (int i = 0; i < _weaponPerkModifiers.Count; i++)
			{
				if (_weaponPerkModifiers[i] != null)
				{
					_weaponPerkModifiers[i].Apply(StatMultipliers.attackStatsMultipliers);
				}
			}
		}

		private void EvaluateEquipmentPerkModifiers()
		{
			if (_equipmentPerkModifiers == null)
			{
				return;
			}
			for (int i = 0; i < _equipmentPerkModifiers.Count; i++)
			{
				if (_equipmentPerkModifiers[i] != null)
				{
					_equipmentPerkModifiers[i].Apply(equipmentStatsMultipliers);
				}
			}
		}

		private void EvaluateOnEnemyDamagePerkModifiers()
		{
			if (_onEnemyDamagePerkModifiers == null)
			{
				return;
			}
			for (int i = 0; i < _onEnemyDamagePerkModifiers.Count; i++)
			{
				if (_onEnemyDamagePerkModifiers[i] != null)
				{
					_onEnemyDamagePerkModifiers[i].Apply(StatMultipliers);
				}
			}
		}

		public void UpdateMaxHealth()
		{
			int maximumHealth = Math.Max(
				1,
				(int)((float)baseStats.maxHP * (1f + StatMultipliers.HPMultiplier)));
			if (currentStats.maxHP != maximumHealth)
			{
				currentStats.maxHP = maximumHealth;
				MaximumHealthChanged?.Invoke(maximumHealth);
			}
		}

		public void UpdateMaxDashes()
		{
			int maximum = Math.Max(0, baseStats.maxDashCharges + StatMultipliers.extraDashCharges);
			if (currentStats.maxDashCharges == maximum) return;
			currentStats.maxDashCharges = maximum;
			MaximumDashesChanged?.Invoke(maximum);
		}

		public event Action<int> MaximumDashesChanged;

		public float GetHealthPercentage()
		{
			return (float)currentStats.HP / (float)MaxHP;
		}

	}
}
