using System;
using System.Collections.Generic;
using Assets.Scripts.AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Data.Perks;
using AstralShift.HellMaiden.DevDebug;
using AstralShift.HellMaiden.Dialogue;
using AstralShift.HellMaiden.UI.Cards;
using AstralShift.HellMaiden.UI.Menus.Achievement;
using AstralShift.HellMaiden.UI.Menus.Achievement.Data;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand.Data
{
	public class RuntimeDB : MonoBehaviour
	{
		[Header("Database Scriptable Objects")]
		[SerializeField]
		private WeaponDB _weaponDB;

		[SerializeField]
		private EquipmentDB _equipmentDB;

		[SerializeField]
		private PerkDB _perkDB;

		[SerializeField]
		private PlayerMetaStatsDatabase _metaStatsDB;

		[Header("Pool Weights Data")]
		[SerializeField]
		private CardPoolWeights _cardPoolWeightsData;

		[SerializeField]
		private EquipmentLevelWeightsData _cardLevelWeightsData;

		[SerializeField]
		private PerkDropWeightsData _perkDropWeightsData;

		private List<PoetPoolID> _unlockedPoetPools;

		private Dictionary<uint, WeaponData> _weaponsData;

		private bool _weaponDatabaseInitialized;

		private Dictionary<uint, EquipmentData> _equipmentsData;

		private Dictionary<uint, PerkData> _perksData;

		private Dictionary<PoetPoolID, List<WeaponData>> _weaponCardPools;

		private Dictionary<PoetPoolID, List<EquipmentData>> _equipmentCardPools;

		private Dictionary<PerkPoolID, List<PerkData>> _perkPools;

		private CardVisualsFactory _cardVisualsFactory;

		[Header("Achievement Data")]
		[SerializeField]
		private AchievementDB _achievementDBData;

		private Dictionary<AchievementManager.AchievementID, AchievementData> _achievementDB;

		public WeaponDB WeaponDB => _weaponDB;

		public EquipmentDB EquipmentDB => _equipmentDB;

		public PerkDB PerkDB => _perkDB;

		public PlayerMetaStatsDatabase MetaStatsDB => _metaStatsDB;

		public CardPoolWeights CardPoolWeightsData => _cardPoolWeightsData;

		public EquipmentLevelWeightsData CardLevelWeightsData => _cardLevelWeightsData;

		public PerkDropWeightsData PerkDropWeightsData => _perkDropWeightsData;

		public List<PoetPoolID> UnlockedPoetPools => _unlockedPoetPools;

		public bool IsWeaponDatabaseInitialized => _weaponDatabaseInitialized;

		public void Init()
		{
			_equipmentCardPools = new Dictionary<PoetPoolID, List<EquipmentData>>();
			_perkPools = new Dictionary<PerkPoolID, List<PerkData>>();
			InitWeaponDatabase();
			InitRuntimeEquipmentDB();
			InitRuntimePerkDB();
			InitPoetPools();
			InitHubNPCsLUT();
			InitCardVisualsFactory();
			InitAchievementDB();
			GameDataManager instance = GameDataManager.Instance;
			instance.OnRefresh = (Action)Delegate.Combine(instance.OnRefresh, new Action(OnSaveDataLoad));
		}

		public void ConfigureWeaponDatabase(WeaponDB weaponDatabase)
		{
			_weaponDB = weaponDatabase != null
				? weaponDatabase
				: throw new ArgumentNullException(nameof(weaponDatabase));
			_weaponsData = null;
			_weaponCardPools = null;
			_weaponDatabaseInitialized = false;
		}

		public void InitWeaponDatabase()
		{
			if (_weaponDatabaseInitialized)
			{
				return;
			}
			if (WeaponDB == null)
			{
				throw new InvalidOperationException(
					"RuntimeDB requires a WeaponDB before initializing weapons.");
			}

			WeaponData[] weapons = WeaponDB.Weapons;
			if (weapons == null)
			{
				throw new InvalidOperationException(
					$"WeaponDB '{WeaponDB.name}' has no weapon array.");
			}

			_weaponsData = new Dictionary<uint, WeaponData>();
			_weaponCardPools = new Dictionary<PoetPoolID, List<WeaponData>>();
			for (int i = 0; i < weapons.Length; i++)
			{
				WeaponData weapon = weapons[i];
				if (weapon == null)
				{
					throw new InvalidOperationException(
						$"WeaponDB '{WeaponDB.name}' contains a null entry at index {i}.");
				}
				if (!_weaponsData.TryAdd(weapon.ID, weapon))
				{
					throw new InvalidOperationException(
						$"WeaponDB '{WeaponDB.name}' contains duplicate weapon ID {weapon.ID}.");
				}

				AddWeaponCardToPool(weapon);
			}

			_weaponDatabaseInitialized = true;
		}

		private void InitRuntimeEquipmentDB()
		{
			DataModifierResolver.BuildCache();
			_equipmentsData = new Dictionary<uint, EquipmentData>();
			for (int i = 0; i < EquipmentDB.Equipments.Length; i++)
			{
				_equipmentsData.Add(EquipmentDB.Equipments[i].ID, EquipmentDB.Equipments[i]);
				AddEquipmentCardToPool(EquipmentDB.Equipments[i]);
			}
		}

		private void InitCardVisualsFactory()
		{
			_cardVisualsFactory = new CardVisualsFactory();
			_cardVisualsFactory.Init();
		}

		private void InitPoetPools()
		{
			_unlockedPoetPools = new List<PoetPoolID>();
			if (!DeveloperDebug.OverrideUnlockPools)
			{
				if (GameData.Instance.unlockedPoets.Count == 0)
				{
					UnlockPoetPool(PoetPoolID.Dante);
				}
				else
				{
					foreach (PoetPoolID unlockedPoet in GameData.Instance.unlockedPoets)
					{
						if (!_unlockedPoetPools.Contains(unlockedPoet))
						{
							_unlockedPoetPools.Add(unlockedPoet);
						}
					}
				}
			}
			else
			{
				for (int i = 0; i < DeveloperDebug.unlockedPools.Length; i++)
				{
					UnlockPoetPool(DeveloperDebug.unlockedPools[i]);
				}
			}
			string text = "";
			for (int j = 0; j < _unlockedPoetPools.Count; j++)
			{
				text = text + ";" + _unlockedPoetPools[j];
			}
		}

		private void InitHubNPCsLUT()
		{
			if (GameData.Instance.availableHubNPCs.Count == 0)
			{
				UnlockHubNPC(PoetID.Virgil);
			}
		}

		private void InitAchievementDB()
		{
			_achievementDB = new Dictionary<AchievementManager.AchievementID, AchievementData>();
			for (int i = 0; i < _achievementDBData.achievements.Count; i++)
			{
				_achievementDB.Add(_achievementDBData.achievements[i].LinkedAchievementID, _achievementDBData.achievements[i]);
			}
		}

		private void OnSaveDataLoad()
		{
			InitPoetPools();
			InitHubNPCsLUT();
		}

		private void AddEquipmentCardToPool(EquipmentData card)
		{
			if (!_equipmentCardPools.ContainsKey(card.poolID))
			{
				_equipmentCardPools.Add(card.poolID, new List<EquipmentData>());
			}
			_equipmentCardPools[card.poolID].Add(card);
		}

		private void AddWeaponCardToPool(WeaponData card)
		{
			if (!_weaponCardPools.ContainsKey(card.poolID))
			{
				_weaponCardPools.Add(card.poolID, new List<WeaponData>());
			}
			_weaponCardPools[card.poolID].Add(card);
		}

		public EquipmentData GetEquipmentData(uint id)
		{
			return _equipmentsData[id];
		}

		public WeaponData GetWeaponData(uint id)
		{
			InitWeaponDatabase();
			if (!_weaponsData.TryGetValue(id, out WeaponData weapon))
			{
				throw new KeyNotFoundException(
					$"RuntimeDB contains no WeaponData with ID {id}.");
			}

			return weapon;
		}

		public bool TryGetWeaponData(uint id, out WeaponData weapon)
		{
			InitWeaponDatabase();
			return _weaponsData.TryGetValue(id, out weapon);
		}

		public Dictionary<PoetPoolID, List<EquipmentData>> GetEquipmentCardPoolData()
		{
			return _equipmentCardPools;
		}

		public Dictionary<PoetPoolID, List<WeaponData>> GetWeaponCardPoolData()
		{
			InitWeaponDatabase();
			return _weaponCardPools;
		}

		private void InitRuntimePerkDB()
		{
			_perksData = new Dictionary<uint, PerkData>();
			for (int i = 0; i < PerkDB.Perks.Length; i++)
			{
				_perksData.Add(PerkDB.Perks[i].ID, PerkDB.Perks[i]);
				AddPerkToPool(PerkDB.Perks[i], _perkPools);
			}
		}

		private void AddPerkToPool(PerkData perk, Dictionary<PerkPoolID, List<PerkData>> perkPool)
		{
			if (!perkPool.ContainsKey(PerkPoolID.Beatrice))
			{
				perkPool.Add(PerkPoolID.Beatrice, new List<PerkData>());
			}
			perkPool[PerkPoolID.Beatrice].Add(perk);
		}

		public Dictionary<PerkPoolID, List<PerkData>> GetPerkPoolData()
		{
			return _perkPools;
		}

		public void UnlockPoetPool(PoetPoolID poolId)
		{
			_unlockedPoetPools.Add(poolId);
			GameDataManager.UnlockPoet(poolId);
			if (poolId != PoetPoolID.Dante)
			{
				Enum.TryParse<PoetID>(poolId.ToString(), out var result);
				UnlockHubNPC(result);
			}
		}

		public bool IsPoetPoolUnlocked(PoetPoolID poolId)
		{
			return _unlockedPoetPools.Contains(poolId);
		}

		public AchievementData GetAchievement(AchievementManager.AchievementID achievementID)
		{
			return _achievementDB[achievementID];
		}

		private void UnlockHubNPC(PoetID poet)
		{
			if (!GameData.Instance.availableHubNPCs.Contains(poet))
			{
				GameData.Instance.availableHubNPCs.Add(poet);
			}
		}
	}
}
