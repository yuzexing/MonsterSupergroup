using System;
using System.Collections.Generic;
using Assets.Scripts.AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data;
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

		private PlayerMetaStatsDatabase _playerMetaStatsDatabase;

		public int MaxHP => currentStats.maxHP;

		public PlayerStatsMultipliers StatMultipliers => statMultipliers;

		public void Init()
		{
			// _playerMetaStatsDatabase = GameDirector.Instance.runtimeDB.MetaStatsDB;
			CalculateMetaStats();
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
			currentStats.maxDashCharges = baseStats.maxDashCharges + StatMultipliers.extraDashCharges;
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
			int maxHP = currentStats.maxHP;
			currentStats.maxHP = (int)((float)baseStats.maxHP * (1f + StatMultipliers.HPMultiplier));
			if (maxHP != MaxHP)
			{
				currentStats.HP += currentStats.maxHP - maxHP;
				GameEvents.Instance.OnMaxHealthUpdate?.Invoke(MaxHP);
				GameEvents.Instance.OnHealthUpdate?.Invoke(currentStats.HP);
			}
		}

		public void UpdateMaxDashes()
		{
			currentStats.dashCharges = baseStats.dashCharges + StatMultipliers.extraDashCharges;
			GameEvents.Instance.OnMaxDashesUpdate?.Invoke();
		}

		public float GetHealthPercentage()
		{
			return (float)currentStats.HP / (float)MaxHP;
		}

		private void CalculateMetaStats()
		{
			baseStats.maxHP = playerBaseStatsDatabase.values.maxHP + (int)GetMetaIncrementValue(MetaProgressionID.HP, 0f);
			baseStats.HP = baseStats.maxHP;
			baseStats.moveSpeed = playerBaseStatsDatabase.values.moveSpeed * (1f + GetMetaIncrementValue(MetaProgressionID.MOVESPEED, 0f));
			baseStats.dashDistance = playerBaseStatsDatabase.values.dashDistance * (1f + GetMetaIncrementValue(MetaProgressionID.DASHDISTANCE, 0f));
			baseStats.dashCooldown = playerBaseStatsDatabase.values.dashCooldown * (1f - GetMetaIncrementValue(MetaProgressionID.DASHCOOLDOWN, 0f));
			baseStats.dashCharges = playerBaseStatsDatabase.values.dashCharges + (int)GetMetaIncrementValue(MetaProgressionID.DASHCHARGES, 0f);
			baseStats.maxDashCharges = baseStats.dashCharges;
			baseStats.pullArea = playerBaseStatsDatabase.values.pullArea * (1f + GetMetaIncrementValue(MetaProgressionID.PULLAREA, 0f));
			baseStats.xpModifier = playerBaseStatsDatabase.values.xpModifier * (1f + GetMetaIncrementValue(MetaProgressionID.XPMODIFIER, 0f));
			baseStats.dmgReduction = playerBaseStatsDatabase.values.dmgReduction * (1f + GetMetaIncrementValue(MetaProgressionID.DMGREDUCTION, 0f));
			baseStats.cardsReRollsAmount = playerBaseStatsDatabase.values.cardsReRollsAmount + (int)GetMetaIncrementValue(MetaProgressionID.CARDREROLLS, 0f);
			baseStats.cardBanishesAmount = playerBaseStatsDatabase.values.cardBanishesAmount + (int)GetMetaIncrementValue(MetaProgressionID.CARDBANISHES, 0f);
			baseStats.perksRerollsAmount = playerBaseStatsDatabase.values.perksRerollsAmount + (int)GetMetaIncrementValue(MetaProgressionID.CHARMREROLLS, 0f);
			StatMultipliers.baseAttackStatsMultipliers.Reset();
			StatMultipliers.baseAttackStatsMultipliers.damage += GetMetaIncrementValue(MetaProgressionID.ATKDAMAGE, 0f);
			StatMultipliers.baseAttackStatsMultipliers.critRate += GetMetaIncrementValue(MetaProgressionID.CRITRATE, 0f);
			StatMultipliers.baseAttackStatsMultipliers.critDamage += GetMetaIncrementValue(MetaProgressionID.CRITMULTIPLIER, 0f);
			StatMultipliers.baseAttackStatsMultipliers.speed += GetMetaIncrementValue(MetaProgressionID.ATKSPEED, 0f);
			StatMultipliers.baseAttackStatsMultipliers.size += GetMetaIncrementValue(MetaProgressionID.ATKSIZE, 0f);
			StatMultipliers.baseAttackStatsMultipliers.duration += GetMetaIncrementValue(MetaProgressionID.ATKDURATION, 0f);
			StatMultipliers.baseAttackStatsMultipliers.projectileCountIncrement += (int)GetMetaIncrementValue(MetaProgressionID.PROJINCREMENT, 0f);
			StatMultipliers.Reset();
		}

		private float GetMetaIncrementValue(MetaProgressionID metaProgressionID, float defaultValue)
		{
			int metaProgressionLevel = GameDataManager.GetMetaProgressionLevel(metaProgressionID);
			if (metaProgressionLevel > 0)
			{
				return _playerMetaStatsDatabase.entries[metaProgressionID].levels[metaProgressionLevel - 1].increaseAmmount;
			}
			return defaultValue;
		}
	}
}
