using System.Collections.Generic;
using System.Linq;
using System.Text;
using AstralShift.DebugTools;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public class CardPool
	{
		private List<RuntimeEquipmentData> _weightedEquipmentsData;

		private List<RuntimeCardData> _allChosenCardsInstances;

		private List<RuntimeWeaponData> _chosenWeaponTypes;

		private List<RuntimeEquipmentData> _chosenEquipmentTypes;

		private int _allChosenEquipmentCardsCount;

		private List<RuntimeWeaponData> _currentWeaponChoices;

		private List<RuntimeEquipmentData> _currentEquipmentChoices;

		private Dictionary<CardData, float> _currentCardWeights;

		private List<CardData> _onReRollDroppedCards;

		private Dictionary<PoetPoolID, List<CardData>> _bannedCardsData;

		private Dictionary<PoetPoolID, List<EquipmentData>> _biasedEquipmentsDataMap;

		private int _currentWeaponDropLevelIndex;

		private Dictionary<PoetPoolID, List<WeaponData>> _weaponsPools;

		private Dictionary<PoetPoolID, List<EquipmentData>> _equipmentsPools;

		private PoetPoolID _currentWeaponPoolID;

		private PoetPoolID _currentEquipmentPoolID;

		private List<PoetPoolID> _secondaryPoolIDs;

		private CardPoolWeights _poolWeightsData;

		private CardPoolWeights.LevelThreshold _previousWeaponPoolWeights;

		private CardPoolWeights.LevelThreshold _currentWeaponPoolWeights;

		private float _weaponMainPoolWeight;

		private float _weaponSecondaryPoolWeight;

		private Dictionary<PoetPoolID, float> _weaponSecondaryPoolWeights;

		private int _weaponPoolWeightsIndex = -1;

		private CardPoolWeights.LevelThreshold _previousEquipmentPoolWeights;

		private CardPoolWeights.LevelThreshold _currentEquipmentPoolWeights;

		private float _equipmentMainPoolWeight;

		private float _equipmentSecondaryPoolWeight;

		private Dictionary<PoetPoolID, float> _equipmentSecondaryPoolWeights;

		private int _equipmentPoolWeightsIndex = -1;

		private EquipmentLevelWeightsData _levelWeightsData;

		private EquipmentLevelWeightsData.LevelThreshold _currentLevelThreshold;

		private EquipmentLevelWeightsData.LevelThreshold _previousLevelThreshold;

		private readonly float[] _currentEquipmentLevelWeights = new float[3];

		private int _currentLevelThresholdIndex = -1;

		private float _firstEquipmentRollBiasChance;

		private float _secondEquipmentRollBiasChance;

		private const int MaxDropCardsCount = 3;

		private StringBuilder _debugPoolInfoStringBuilder = new StringBuilder();

		public List<RuntimeWeaponData> ChosenWeaponTypes => _chosenWeaponTypes;

		public List<RuntimeEquipmentData> ChosenEquipmentTypes => _chosenEquipmentTypes;

		public Dictionary<PoetPoolID, List<CardData>> BannedCards => _bannedCardsData;

		public int[] WeaponDropLevels => _poolWeightsData.WeaponDropLevels;

		public int CurrentWeaponDropLevelIndex => _currentWeaponDropLevelIndex;

		public Dictionary<PoetPoolID, List<WeaponData>> WeaponsPools => _weaponsPools;

		public Dictionary<PoetPoolID, List<EquipmentData>> EquipmentsPools => _equipmentsPools;

		public PoetPoolID CurrentWeaponPoolID => _currentWeaponPoolID;

		public PoetPoolID CurrentEquipmentPoolID => _currentEquipmentPoolID;

		public CardPoolWeights PoolWeightsData => _poolWeightsData;

		public float WeaponMainPoolWeight => _weaponMainPoolWeight;

		public float WeaponSecondaryPoolWeight => _weaponSecondaryPoolWeight;

		public Dictionary<PoetPoolID, float> WeaponSecondaryPoolWeights => _weaponSecondaryPoolWeights;

		public float EquipmentMainPoolWeight => _equipmentMainPoolWeight;

		public float EquipmentSecondaryPoolWeight => _equipmentSecondaryPoolWeight;

		public Dictionary<PoetPoolID, float> EquipmentSecondaryPoolWeights => _equipmentSecondaryPoolWeights;

		public float FirstEquipmentRollBiasChance => _firstEquipmentRollBiasChance;

		public float SecondEquipmentRollBiasChance => _secondEquipmentRollBiasChance;

		public void Init()
		{
			_currentWeaponChoices = new List<RuntimeWeaponData>();
			_currentEquipmentChoices = new List<RuntimeEquipmentData>();
			_weightedEquipmentsData = new List<RuntimeEquipmentData>();
			_biasedEquipmentsDataMap = new Dictionary<PoetPoolID, List<EquipmentData>>();
			_weaponsPools = GameDirector.Instance.runtimeDB.GetWeaponCardPoolData().ToDictionary((KeyValuePair<PoetPoolID, List<WeaponData>> entry) => entry.Key, (KeyValuePair<PoetPoolID, List<WeaponData>> entry) => entry.Value.ToList());
			_equipmentsPools = GameDirector.Instance.runtimeDB.GetEquipmentCardPoolData().ToDictionary((KeyValuePair<PoetPoolID, List<EquipmentData>> entry) => entry.Key, (KeyValuePair<PoetPoolID, List<EquipmentData>> entry) => entry.Value.ToList());
			_poolWeightsData = GameDirector.Instance.runtimeDB.CardPoolWeightsData;
			_levelWeightsData = GameDirector.Instance.runtimeDB.CardLevelWeightsData;
			_allChosenCardsInstances = new List<RuntimeCardData>();
			_chosenWeaponTypes = new List<RuntimeWeaponData>();
			_chosenEquipmentTypes = new List<RuntimeEquipmentData>();
			_onReRollDroppedCards = new List<CardData>();
			_bannedCardsData = new Dictionary<PoetPoolID, List<CardData>>();
			InitializePoolWeights();
			InitializeCardsWeights();
			UpdateWeights(0);
			UpdateEquipmentDropChances();
		}

		private void InitializePoolWeights()
		{
			_secondaryPoolIDs = new List<PoetPoolID>(GameDirector.Instance.runtimeDB.UnlockedPoetPools);
			_secondaryPoolIDs.Remove(PoetPoolID.Dante);
			_weaponSecondaryPoolWeights = new Dictionary<PoetPoolID, float>(_secondaryPoolIDs.Count);
			_equipmentSecondaryPoolWeights = new Dictionary<PoetPoolID, float>(_secondaryPoolIDs.Count);
			if (_secondaryPoolIDs.Count == 0)
			{
				return;
			}
			foreach (PoetPoolID secondaryPoolID in _secondaryPoolIDs)
			{
				_weaponSecondaryPoolWeights.TryAdd(secondaryPoolID, 1f);
				_equipmentSecondaryPoolWeights.TryAdd(secondaryPoolID, 1f);
			}
		}

		private void InitializeCardsWeights()
		{
			if (_currentCardWeights == null)
			{
				_currentCardWeights = new Dictionary<CardData, float>();
			}
			_currentCardWeights.Clear();
			foreach (List<WeaponData> value in _weaponsPools.Values)
			{
				for (int i = 0; i < value.Count; i++)
				{
					_currentCardWeights.Add(value[i], value[i].poolWeight);
				}
			}
			foreach (List<EquipmentData> value2 in _equipmentsPools.Values)
			{
				for (int j = 0; j < value2.Count; j++)
				{
					_currentCardWeights.Add(value2[j], value2[j].poolWeight);
				}
			}
		}

		public RuntimeCardData[] GetCardsDrop(int currentLevel, out bool isWeaponDrop, out PoetPoolID poolID)
		{
			ResetCurrentRollWeights();
			isWeaponDrop = IsWeaponDrop(currentLevel);
			RuntimeCardData[] array;
			if (isWeaponDrop)
			{
				if (_poolWeightsData.WeaponsWeightedRandom)
				{
					poolID = RollWeightedPoetPoolID(_weaponMainPoolWeight, _weaponSecondaryPoolWeight, _weaponSecondaryPoolWeights);
				}
				else
				{
					poolID = RollRandomPoetPoolID();
				}
				_currentWeaponPoolID = poolID;
				RuntimeCardData[] weaponCardsDataDrop = GetWeaponCardsDataDrop(poolID);
				array = weaponCardsDataDrop;
				ReduceCurrentRollWeights(array, _poolWeightsData.WeaponReRollWeightReductionFactor);
			}
			else
			{
				poolID = RollWeightedPoetPoolID(_equipmentMainPoolWeight, _equipmentSecondaryPoolWeight, _equipmentSecondaryPoolWeights);
				_currentEquipmentPoolID = poolID;
				RuntimeCardData[] weaponCardsDataDrop = GetEquipmentCardsDrop(poolID);
				array = weaponCardsDataDrop;
				ReduceCurrentRollWeights(array, _poolWeightsData.EquipmentReRollWeightReductionFactor);
			}
			return array;
		}

		public RuntimeCardData[] ReRollCardsDrop(int currentLevel, out bool isWeaponDrop)
		{
			isWeaponDrop = IsWeaponDrop(currentLevel);
			RuntimeCardData[] array;
			if (isWeaponDrop)
			{
				RuntimeCardData[] weaponCardsDataDrop = GetWeaponCardsDataDrop(_currentWeaponPoolID);
				array = weaponCardsDataDrop;
				ReduceCurrentRollWeights(array, _poolWeightsData.WeaponReRollWeightReductionFactor);
			}
			else
			{
				RuntimeCardData[] weaponCardsDataDrop = GetEquipmentCardsDrop(_currentEquipmentPoolID);
				array = weaponCardsDataDrop;
				ReduceCurrentRollWeights(array, _poolWeightsData.EquipmentReRollWeightReductionFactor);
			}
			return array;
		}

		private RuntimeWeaponData[] GetWeaponCardsDataDrop(PoetPoolID chosenID)
		{
			_currentWeaponChoices.Clear();
			FillListWithRandomWeaponsData(_currentWeaponChoices, _weaponsPools[chosenID]);
			if (_currentWeaponChoices.Count < 3)
			{
				FillListWithRandomWeaponsData(_currentWeaponChoices, _weaponsPools[PoetPoolID.Dante]);
				DBL.Log(DBL.Module.CardPool, $"GetWeaponCardsDataDrop - Fallback applied. Not enough weapons from pool ({chosenID}). Dante pool used instead!");
			}
			return _currentWeaponChoices.ToArray();
		}

		private void FillListWithRandomWeaponsData(List<RuntimeWeaponData> listToFill, List<WeaponData> poolList)
		{
			List<WeaponData> list = poolList.Where(AreDependenciesMet).ToList();
			foreach (RuntimeWeaponData item in listToFill)
			{
				list.Remove(item.Data);
			}
			for (float num = listToFill.Count; num < 3f; num += 1f)
			{
				RuntimeWeaponData runtimeWeaponData = CalculateWeaponDrop(list);
				if (runtimeWeaponData != null)
				{
					listToFill.Add(runtimeWeaponData);
					list.Remove(runtimeWeaponData.Data);
				}
			}
		}

		private RuntimeWeaponData CalculateWeaponDrop(List<WeaponData> poolList)
		{
			List<float> list = new List<float>();
			RuntimeWeaponData runtimeWeaponData = null;
			float num = 0f;
			foreach (WeaponData pool in poolList)
			{
				float num2 = _currentCardWeights[pool];
				num += num2;
				list.Add(num2);
			}
			float num3 = Random.Range(0f, num);
			for (int i = 0; i < poolList.Count; i++)
			{
				if (num3 <= list[i])
				{
					runtimeWeaponData = new RuntimeWeaponData(poolList[i]);
					break;
				}
				num3 -= list[i];
			}
			if (runtimeWeaponData == null && poolList.Count > 0)
			{
				runtimeWeaponData = new RuntimeWeaponData(poolList[poolList.Count - 1]);
				DBL.Log(DBL.Module.CardPool, "CalculateWeaponDrop() - Fallback weapon dropped (" + runtimeWeaponData.Data.GetTitle() + "). It shouldn't happen!", 2);
			}
			return runtimeWeaponData;
		}

		private bool IsWeaponDrop(int level)
		{
			if (_currentWeaponDropLevelIndex != WeaponDropLevels.Length)
			{
				return level == WeaponDropLevels[_currentWeaponDropLevelIndex];
			}
			return false;
		}

		private RuntimeEquipmentData[] GetEquipmentCardsDrop(PoetPoolID poolID)
		{
			_currentEquipmentChoices.Clear();
			if (_biasedEquipmentsDataMap.TryGetValue(poolID, out var value))
			{
				List<EquipmentData> cardDatas = value.ToList();
				TryGetBiasedEquipment(_currentEquipmentChoices, cardDatas, _firstEquipmentRollBiasChance);
				TryGetBiasedEquipment(_currentEquipmentChoices, cardDatas, _secondEquipmentRollBiasChance);
				DBL.Log(DBL.Module.CardPool, (_currentEquipmentChoices.Count > 0) ? "GetEquipmentCardsDrop() - Biased equipments dropped!" : "GetEquipmentCardsDrop() - No biased equipments were dropped!");
			}
			bool isThirdSlotDefaultDrop = _poolWeightsData.IsThirdSlotDefaultDrop;
			FillListWithRandomEquipments(_currentEquipmentChoices, _equipmentsPools[poolID], isThirdSlotDefaultDrop ? 2 : 3);
			if (_currentEquipmentChoices.Count < 3)
			{
				FillListWithRandomEquipments(_currentEquipmentChoices, _equipmentsPools[PoetPoolID.Dante]);
				DBL.Log(DBL.Module.CardPool, $"GetEquipmentCardsDrop() - Fallback applied. Not enough cards from pool ({poolID}). Dante pool used instead!");
			}
			return _currentEquipmentChoices.ToArray();
		}

		private void TryGetBiasedEquipment(List<RuntimeEquipmentData> listToFill, List<EquipmentData> cardDatas, float chance)
		{
			if (cardDatas.Count == 0)
			{
				return;
			}
			float num = Random.Range(0f, 1f);
			if (chance != 0f && num <= chance)
			{
				RuntimeEquipmentData runtimeEquipmentFromPool = GetRuntimeEquipmentFromPool(cardDatas);
				if (runtimeEquipmentFromPool != null)
				{
					listToFill.Add(runtimeEquipmentFromPool);
					cardDatas.Remove(runtimeEquipmentFromPool.Data);
				}
			}
		}

		private void FillListWithRandomEquipments(List<RuntimeEquipmentData> listToFill, List<EquipmentData> poolList, int maxCount = 3)
		{
			List<EquipmentData> list = poolList.Where(AreDependenciesMet).ToList();
			foreach (RuntimeEquipmentData item in listToFill)
			{
				list.Remove(item.Data);
			}
			for (float num = listToFill.Count; num < (float)maxCount; num += 1f)
			{
				RuntimeEquipmentData runtimeEquipmentData = CalculateEquipmentDrop(list);
				if (runtimeEquipmentData != null)
				{
					listToFill.Add(runtimeEquipmentData);
					list.Remove(runtimeEquipmentData.Data);
				}
			}
		}

		private RuntimeEquipmentData GetRuntimeEquipmentFromPool(List<EquipmentData> values)
		{
			RuntimeEquipmentData newRuntimeData = CalculateEquipmentDrop(values);
			if (newRuntimeData == null)
			{
				return null;
			}
			if (PlayerHand.Instance.IsHandFull)
			{
				List<RuntimeEquipmentData> list = _weightedEquipmentsData.FindAll((RuntimeEquipmentData x) => x.BaseData.ID == newRuntimeData.BaseData.ID);
				int num = 0;
				if (list.Count > 0)
				{
					num = Random.Range(0, list.Count);
					newRuntimeData.ApplyLevel(list[num].LevelIndex);
				}
				else
				{
					num = Random.Range(0, 2);
					newRuntimeData.ApplyLevel((uint)num);
					DBL.Log(DBL.Module.CardPool, "GetRuntimeEquipmentFromPool() - Fallback random level applied (" + newRuntimeData.Data.GetTitle() + "). Should not happen!", 2);
				}
			}
			return newRuntimeData;
		}

		private RuntimeEquipmentData CalculateEquipmentDrop(List<EquipmentData> poolList)
		{
			List<float> list = new List<float>();
			RuntimeEquipmentData runtimeEquipmentData = null;
			float num = 0f;
			foreach (EquipmentData pool in poolList)
			{
				float num2 = _currentCardWeights[pool];
				num += num2;
				list.Add(num2);
			}
			float num3 = Random.Range(0f, num);
			for (int i = 0; i < poolList.Count; i++)
			{
				if (num3 <= list[i])
				{
					runtimeEquipmentData = new RuntimeEquipmentData(poolList[i]);
					break;
				}
				num3 -= list[i];
			}
			if (runtimeEquipmentData == null && poolList.Count > 0)
			{
				runtimeEquipmentData = new RuntimeEquipmentData(poolList[poolList.Count - 1]);
				DBL.Log(DBL.Module.CardPool, "CalculateEquipmentDrop() - Fallback equipment dropped (" + runtimeEquipmentData.Data.GetTitle() + "). Should not happen!", 2);
			}
			if (runtimeEquipmentData != null)
			{
				RollEquipmentLevel(runtimeEquipmentData);
			}
			return runtimeEquipmentData;
		}

		private void RollEquipmentLevel(RuntimeEquipmentData runtimeData)
		{
			uint num = (uint)(runtimeData.Data.Levels.Length - 1);
			uint levelIndex = num;
			float num2 = Random.Range(0f, 100f);
			for (int i = 0; i < _currentEquipmentLevelWeights.Length; i++)
			{
				if (num2 <= _currentEquipmentLevelWeights[i])
				{
					levelIndex = ((i <= num) ? ((uint)i) : num);
					break;
				}
				num2 -= _currentEquipmentLevelWeights[i];
			}
			runtimeData.ApplyLevel(levelIndex);
		}

		public void RegisterChosenCard(RuntimeCardData runtimeData)
		{
			_allChosenCardsInstances.Add(runtimeData);
			if (runtimeData is RuntimeWeaponData runtimeWeaponData)
			{
				if (!IsWeaponChosen(runtimeWeaponData.Data))
				{
					_chosenWeaponTypes.Add(runtimeWeaponData);
					_currentWeaponDropLevelIndex++;
					BanCard(runtimeWeaponData.BaseData);
				}
			}
			else
			{
				RuntimeEquipmentData equipmentData = runtimeData as RuntimeEquipmentData;
				if (equipmentData != null)
				{
					_allChosenEquipmentCardsCount++;
					UpdateEquipmentDropChances();
					if (!IsEquipmentChosen(equipmentData.Data))
					{
						_chosenEquipmentTypes.Add(equipmentData);
					}
					if (!runtimeData.IsMaxLevel())
					{
						_weightedEquipmentsData.Add(equipmentData);
						if (!_biasedEquipmentsDataMap.ContainsKey(runtimeData.BaseData.poolID))
						{
							_biasedEquipmentsDataMap.Add(runtimeData.BaseData.poolID, new List<EquipmentData>());
						}
						if (_biasedEquipmentsDataMap[runtimeData.BaseData.poolID].Find((EquipmentData x) => x.ID == equipmentData.Data.ID) == null)
						{
							_biasedEquipmentsDataMap[runtimeData.BaseData.poolID].Add(equipmentData.Data);
						}
					}
				}
			}
			TryIncrementSecondaryPoolWeight(runtimeData.BaseData);
		}

		public void UnRegisterChosenCard(RuntimeCardData runtimeData)
		{
			if (!_allChosenCardsInstances.Remove(runtimeData))
			{
				return;
			}
			if (runtimeData is RuntimeWeaponData runtimeWeaponData)
			{
				_chosenWeaponTypes.Remove(runtimeWeaponData);
				UnbanCard(runtimeWeaponData.BaseData);
				return;
			}
			RuntimeEquipmentData equipment = runtimeData as RuntimeEquipmentData;
			if (equipment == null)
			{
				return;
			}
			_allChosenEquipmentCardsCount = Mathf.Max(0, _allChosenEquipmentCardsCount - 1);
			UpdateEquipmentDropChances();
			_weightedEquipmentsData.Remove(equipment);
			if (_allChosenCardsInstances.Find((RuntimeCardData x) => x.BaseData == equipment.BaseData) == null)
			{
				_chosenEquipmentTypes.Remove(equipment);
				if (_biasedEquipmentsDataMap.TryGetValue(equipment.BaseData.poolID, out var value))
				{
					value.Remove(equipment.Data);
				}
			}
		}

		public void RegisterSignatureWeapon(RuntimeWeaponData runtimeData)
		{
			_allChosenCardsInstances.Add(runtimeData);
			if (!IsWeaponChosen(runtimeData.Data))
			{
				ChosenWeaponTypes.Add(runtimeData);
				BanCard(runtimeData.BaseData);
			}
		}

		public void BanCard(CardData data)
		{
			if (!_bannedCardsData.ContainsKey(data.poolID))
			{
				_bannedCardsData.Add(data.poolID, new List<CardData>());
			}
			_bannedCardsData[data.poolID].Add(data);
			if (data is EquipmentData equipmentData)
			{
				_equipmentsPools[equipmentData.poolID].Remove(equipmentData);
			}
			if (data is WeaponData weaponData)
			{
				_weaponsPools[weaponData.poolID].Remove(weaponData);
			}
		}

		public void UnbanCard(CardData data)
		{
			if (!_bannedCardsData.ContainsKey(data.poolID) || _bannedCardsData[data.poolID].Count == 0 || !_bannedCardsData[data.poolID].Contains(data))
			{
				return;
			}
			_bannedCardsData[data.poolID].Remove(data);
			if (!(data is EquipmentData item))
			{
				if (data is WeaponData item2 && !_weaponsPools[data.poolID].Contains(item2))
				{
					_weaponsPools[data.poolID].Add(item2);
				}
			}
			else if (!_equipmentsPools[data.poolID].Contains(item))
			{
				_equipmentsPools[data.poolID].Add(item);
			}
		}

		private bool IsCardBanned(RuntimeCardData runtimeData)
		{
			if (!_bannedCardsData.TryGetValue(runtimeData.BaseData.poolID, out var value))
			{
				return false;
			}
			return value.Find((CardData x) => x == runtimeData.BaseData);
		}

		public void TryExcludeMaxLevelEquipment(RuntimeEquipmentData runtimeData)
		{
			List<RuntimeCardData> list = _allChosenCardsInstances.FindAll((RuntimeCardData x) => x.BaseData == runtimeData.BaseData);
			if (list.Count <= 0)
			{
				return;
			}
			_weightedEquipmentsData.Remove(runtimeData);
			int num = 0;
			for (int num2 = 0; num2 < list.Count; num2++)
			{
				if (list[num2].LevelIndex == runtimeData.GetMaxLevel() - 1)
				{
					num++;
				}
			}
			if (num == list.Count && _biasedEquipmentsDataMap.ContainsKey(runtimeData.BaseData.poolID))
			{
				_biasedEquipmentsDataMap[runtimeData.BaseData.poolID].Remove(runtimeData.Data);
			}
		}

		private bool IsWeaponChosen(WeaponData data)
		{
			for (int i = 0; i < _chosenWeaponTypes.Count; i++)
			{
				if (_chosenWeaponTypes[i].Data.ID == data.ID)
				{
					return true;
				}
			}
			return false;
		}

		private bool IsEquipmentChosen(EquipmentData data)
		{
			for (int i = 0; i < _chosenEquipmentTypes.Count; i++)
			{
				if (_chosenEquipmentTypes[i].Data.ID == data.ID)
				{
					return true;
				}
			}
			return false;
		}

		private bool AreDependenciesMet(EquipmentData data)
		{
			if (data.Dependencies == null || data.Dependencies.Length == 0)
			{
				return true;
			}
			return EvaluateDependencies(data.Dependencies);
		}

		private bool AreDependenciesMet(WeaponData data)
		{
			if (data.Dependencies == null || data.Dependencies.Length == 0)
			{
				return true;
			}
			return EvaluateDependencies(data.Dependencies);
		}

		private bool EvaluateDependencies(DataDependency[] dependencies)
		{
			foreach (DataDependency dataDependency in dependencies)
			{
				if (!(dataDependency is WeaponDataDependency weaponDataDependency))
				{
					if (!(dataDependency is EquipmentDataDependency equipmentDataDependency))
					{
						if (!(dataDependency is DialogueSystemTriggerDependency dialogueSystemTriggerDependency))
						{
							if (!(dataDependency is DialogueSystemIntDependency dialogueSystemIntDependency))
							{
								if (!(dataDependency is LevelDependency { IsDependencyMet: false }))
								{
									continue;
								}
							}
							// else if (dialogueSystemIntDependency.IsDependencyMet)
							// {
							// 	continue;
							// }
						}
						// else if (dialogueSystemTriggerDependency.IsDependencyMet)
						// {
						// 	continue;
						// }
					}
					else if (IsEquipmentChosen(equipmentDataDependency.Data))
					{
						continue;
					}
				}
				else if (IsWeaponChosen(weaponDataDependency.Data))
				{
					continue;
				}
				return false;
			}
			return true;
		}

		private PoetPoolID RollRandomPoetPoolID()
		{
			List<PoetPoolID> list = new List<PoetPoolID>();
			list.Add(PoetPoolID.Dante);
			list.AddRange(_secondaryPoolIDs);
			int index = Random.Range(0, list.Count);
			return list[index];
		}

		private PoetPoolID RollWeightedPoetPoolID(float mainPoolWeight, float secondaryPoolWeight, Dictionary<PoetPoolID, float> secondaryPoolWeights)
		{
			if (_secondaryPoolIDs == null || _secondaryPoolIDs.Count == 0)
			{
				return PoetPoolID.Dante;
			}
			float num = mainPoolWeight + secondaryPoolWeight;
			if (num <= 0f)
			{
				return PoetPoolID.Dante;
			}
			if (Random.Range(0f, num) <= secondaryPoolWeight)
			{
				return RollWeightedSecondaryPoolID(_secondaryPoolIDs, secondaryPoolWeights);
			}
			return PoetPoolID.Dante;
		}

		private PoetPoolID RollWeightedSecondaryPoolID(List<PoetPoolID> poolIDs, Dictionary<PoetPoolID, float> poolWeights)
		{
			float num = 0f;
			foreach (PoetPoolID poolID in poolIDs)
			{
				num += poolWeights[poolID];
			}
			float num2 = Random.Range(0f, num);
			for (int i = 0; i < poolIDs.Count; i++)
			{
				PoetPoolID poetPoolID = poolIDs[i];
				float num3 = poolWeights[poetPoolID];
				if (num2 <= num3)
				{
					return poetPoolID;
				}
				num2 -= num3;
			}
			return poolIDs[0];
		}

		public void UpdateWeights(int level)
		{
			UpdateWeaponPoolWeights(level);
			UpdateEquipmentPoolWeights(level);
			UpdateEquipmentDropWeights(level);
		}

		private void UpdateWeaponPoolWeights(int level)
		{
			CardPoolWeights.LevelThreshold[] weaponWeights = _poolWeightsData.WeaponWeights;
			if (weaponWeights == null || weaponWeights.Length == 0)
			{
				return;
			}
			int num = -1;
			for (int i = 0; i < weaponWeights.Length; i++)
			{
				if (weaponWeights[i].Level > level)
				{
					num = i;
					break;
				}
			}
			if (num <= 0)
			{
				int num2 = ((num == -1) ? (weaponWeights.Length - 1) : 0);
				_previousWeaponPoolWeights = weaponWeights[num2];
				_currentWeaponPoolWeights = weaponWeights[num2];
			}
			else
			{
				_previousWeaponPoolWeights = weaponWeights[num - 1];
				_currentWeaponPoolWeights = weaponWeights[num];
			}
			if (_poolWeightsData.WeaponWeightInterpolation)
			{
				_weaponMainPoolWeight = GetRemappedValue(level, _previousWeaponPoolWeights.Level, _currentWeaponPoolWeights.Level, _previousWeaponPoolWeights.MainPoolWeight, _currentWeaponPoolWeights.MainPoolWeight);
				_weaponSecondaryPoolWeight = GetRemappedValue(level, _previousWeaponPoolWeights.Level, _currentWeaponPoolWeights.Level, _previousWeaponPoolWeights.SecondaryPoolWeight, _currentWeaponPoolWeights.SecondaryPoolWeight);
			}
			else
			{
				_weaponMainPoolWeight = _currentWeaponPoolWeights.MainPoolWeight;
				_weaponSecondaryPoolWeight = _currentWeaponPoolWeights.SecondaryPoolWeight;
			}
		}

		private void UpdateEquipmentPoolWeights(int level)
		{
			CardPoolWeights.LevelThreshold[] equipmentWeights = _poolWeightsData.EquipmentWeights;
			if (equipmentWeights == null || equipmentWeights.Length == 0)
			{
				return;
			}
			int num = -1;
			for (int i = 0; i < equipmentWeights.Length; i++)
			{
				if (equipmentWeights[i].Level > level)
				{
					num = i;
					break;
				}
			}
			if (num <= 0)
			{
				int num2 = ((num == -1) ? (equipmentWeights.Length - 1) : 0);
				_previousEquipmentPoolWeights = equipmentWeights[num2];
				_currentEquipmentPoolWeights = equipmentWeights[num2];
			}
			else
			{
				_previousEquipmentPoolWeights = equipmentWeights[num - 1];
				_currentEquipmentPoolWeights = equipmentWeights[num];
			}
			if (_poolWeightsData.EquipmentWeightInterpolation)
			{
				_equipmentMainPoolWeight = GetRemappedValue(level, _previousEquipmentPoolWeights.Level, _currentEquipmentPoolWeights.Level, _previousEquipmentPoolWeights.MainPoolWeight, _currentEquipmentPoolWeights.MainPoolWeight);
				_equipmentSecondaryPoolWeight = GetRemappedValue(level, _previousEquipmentPoolWeights.Level, _currentEquipmentPoolWeights.Level, _previousEquipmentPoolWeights.SecondaryPoolWeight, _currentEquipmentPoolWeights.SecondaryPoolWeight);
			}
			else
			{
				_equipmentMainPoolWeight = _currentEquipmentPoolWeights.MainPoolWeight;
				_equipmentSecondaryPoolWeight = _currentEquipmentPoolWeights.SecondaryPoolWeight;
			}
		}

		private void TryIncrementSecondaryPoolWeight(CardData cardData)
		{
			if (cardData.poolID == PoetPoolID.Dante || !_secondaryPoolIDs.Contains(cardData.poolID))
			{
				return;
			}
			if (!(cardData is WeaponData))
			{
				if (cardData is EquipmentData && _equipmentSecondaryPoolWeights.ContainsKey(cardData.poolID))
				{
					_equipmentSecondaryPoolWeights[cardData.poolID] += _poolWeightsData.EquipmentSecondaryPoolWeightIncrement;
					DBL.Log(DBL.Module.CardPool, $"TryIncrementSecondaryPoolWeight() - Increased Equipment Pool Weight for {cardData.poolID}. New Weight: {_equipmentSecondaryPoolWeights[cardData.poolID]}");
				}
			}
			else if (_weaponSecondaryPoolWeights.ContainsKey(cardData.poolID))
			{
				_weaponSecondaryPoolWeights[cardData.poolID] += _poolWeightsData.WeaponSecondaryPoolWeightIncrement;
				DBL.Log(DBL.Module.CardPool, $"TryIncrementSecondaryPoolWeight: Increased Weapon Pool Weight for {cardData.poolID}. New Weight: {_weaponSecondaryPoolWeights[cardData.poolID]}");
			}
		}

		private void UpdateEquipmentDropWeights(int level)
		{
			EquipmentLevelWeightsData.LevelThreshold[] weights = _levelWeightsData.Weights;
			if (weights == null || weights.Length == 0)
			{
				return;
			}
			int num = -1;
			for (int i = 0; i < weights.Length; i++)
			{
				if (weights[i].Level > level)
				{
					num = i;
					break;
				}
			}
			if (num <= 0)
			{
				int num2 = ((num == -1) ? (weights.Length - 1) : 0);
				_previousLevelThreshold = weights[num2];
				_currentLevelThreshold = weights[num2];
			}
			else
			{
				_previousLevelThreshold = weights[num - 1];
				_currentLevelThreshold = weights[num];
			}
			if (_currentLevelThreshold.LevelWeight.Length == _previousLevelThreshold.LevelWeight.Length)
			{
				for (int j = 0; j < _currentLevelThreshold.LevelWeight.Length; j++)
				{
					_currentEquipmentLevelWeights[j] = GetRemappedValue(level, _previousLevelThreshold.Level, _currentLevelThreshold.Level, _previousLevelThreshold.LevelWeight[j], _currentLevelThreshold.LevelWeight[j]);
					DBL.Log(DBL.Module.CardPool, $"UpdateEquipmentDropWeights() - Weights Updated: Level {level}, Index {j}, Value {_currentEquipmentLevelWeights[j]}");
				}
			}
		}

		private void UpdateEquipmentDropChances()
		{
			Vector2Int firstSlotEquipmentBiasMinMaxCardCount = _poolWeightsData.FirstSlotEquipmentBiasMinMaxCardCount;
			Vector2 firstSlotEquipmentBiasMinMaxChance = _poolWeightsData.FirstSlotEquipmentBiasMinMaxChance;
			Vector2Int secondSlotEquipmentBiasMinMaxCardCount = _poolWeightsData.SecondSlotEquipmentBiasMinMaxCardCount;
			Vector2 secondSlotEquipmentBiasMinMaxChance = _poolWeightsData.SecondSlotEquipmentBiasMinMaxChance;
			_firstEquipmentRollBiasChance = GetRemappedValue(_allChosenEquipmentCardsCount, firstSlotEquipmentBiasMinMaxCardCount.x, firstSlotEquipmentBiasMinMaxCardCount.y, firstSlotEquipmentBiasMinMaxChance.x, firstSlotEquipmentBiasMinMaxChance.y);
			_secondEquipmentRollBiasChance = GetRemappedValue(_allChosenEquipmentCardsCount, secondSlotEquipmentBiasMinMaxCardCount.x, secondSlotEquipmentBiasMinMaxCardCount.y, secondSlotEquipmentBiasMinMaxChance.x, secondSlotEquipmentBiasMinMaxChance.y);
			DBL.Log(DBL.Module.CardPool, $"UpdateEquipmentDropChances() - Equipments Drop Chance Updated at {_allChosenEquipmentCardsCount} Cards Count: {_firstEquipmentRollBiasChance}|{_secondEquipmentRollBiasChance}");
		}

		private float GetRemappedValue(float current, int minInput, float maxInput, float minOutput, float maxOutput)
		{
			float t = Mathf.InverseLerp(minInput, maxInput, current);
			return Mathf.Lerp(minOutput, maxOutput, t);
		}

		private void ResetCurrentRollWeights()
		{
			for (int i = 0; i < _onReRollDroppedCards.Count; i++)
			{
				CardData cardData = _onReRollDroppedCards[i];
				_currentCardWeights[cardData] = cardData.poolWeight;
			}
			_onReRollDroppedCards.Clear();
		}

		private void ReduceCurrentRollWeights(RuntimeCardData[] currentResult, float reductionFactor)
		{
			if (currentResult == null)
			{
				return;
			}
			for (int i = 0; i < currentResult.Length; i++)
			{
				if (currentResult[i] != null)
				{
					CardData baseData = currentResult[i].BaseData;
					if (!_onReRollDroppedCards.Contains(baseData))
					{
						_onReRollDroppedCards.Add(baseData);
					}
					ReduceCardWeight(baseData, reductionFactor);
				}
			}
		}

		private void ReduceCardWeight(CardData data, float multiplier)
		{
			_currentCardWeights[data] *= multiplier;
		}

		private void LogModPoolStats()
		{
			_debugPoolInfoStringBuilder.Clear();
			_debugPoolInfoStringBuilder.AppendLine("-- Equipment Drop Diagnostics --");
			_debugPoolInfoStringBuilder.Append("\n");
			_debugPoolInfoStringBuilder.AppendLine($"Active Pool: {CurrentEquipmentPoolID}");
			_debugPoolInfoStringBuilder.Append("\n");
			_debugPoolInfoStringBuilder.AppendLine(LogPoolWeightInfo("Equipments Drop Weights", EquipmentMainPoolWeight, EquipmentSecondaryPoolWeight, EquipmentSecondaryPoolWeights));
			_debugPoolInfoStringBuilder.Append("\n");
			_debugPoolInfoStringBuilder.AppendLine("Roll Bias Chance:");
			_debugPoolInfoStringBuilder.AppendLine($"1st Slot Bias - {FirstEquipmentRollBiasChance * 100f:0:##}%");
			_debugPoolInfoStringBuilder.AppendLine($"2nd Slot Bias - {SecondEquipmentRollBiasChance * 100f:0:##}%");
			_debugPoolInfoStringBuilder.Append("\n");
			_debugPoolInfoStringBuilder.AppendLine(GetBannedCardsInfo());
			DBL.Log(DBL.Module.CardPool, _debugPoolInfoStringBuilder.ToString());
			_debugPoolInfoStringBuilder.Clear();
		}

		private void LogWeaponPoolStats()
		{
			_debugPoolInfoStringBuilder.Clear();
			_debugPoolInfoStringBuilder.AppendLine("-- Weapon Drop Diagnostics --");
			_debugPoolInfoStringBuilder.Append("\n");
			_debugPoolInfoStringBuilder.AppendLine($"Active Pool: {CurrentWeaponPoolID}");
			_debugPoolInfoStringBuilder.Append("\n");
			int currentWeaponDropLevelIndex = CurrentWeaponDropLevelIndex;
			if (currentWeaponDropLevelIndex < WeaponDropLevels.Length - 1)
			{
				int num = WeaponDropLevels[currentWeaponDropLevelIndex];
				_debugPoolInfoStringBuilder.AppendLine($"Next Drop Level: {num} (Index: {currentWeaponDropLevelIndex})");
			}
			else
			{
				_debugPoolInfoStringBuilder.AppendLine("Next Drop Level: No more drops queued!");
			}
			_debugPoolInfoStringBuilder.Append("\n");
			if (PoolWeightsData.WeaponsWeightedRandom)
			{
				_debugPoolInfoStringBuilder.AppendLine(LogPoolWeightInfo("Weapon Drop Weights", WeaponMainPoolWeight, WeaponSecondaryPoolWeight, WeaponSecondaryPoolWeights));
			}
			else
			{
				_debugPoolInfoStringBuilder.AppendLine("Weapon Drop Weights: Random");
			}
			_debugPoolInfoStringBuilder.Append("\n");
			_debugPoolInfoStringBuilder.AppendLine(GetBannedCardsInfo());
			DBL.Log(DBL.Module.CardPool, _debugPoolInfoStringBuilder.ToString());
			_debugPoolInfoStringBuilder.Clear();
		}

		private string LogPoolWeightInfo(string label, float main, float secondary, Dictionary<PoetPoolID, float> secondaryWeights)
		{
			string text = label + ":";
			text += $"Main Pool Drop Weight - {main:F2}\n";
			text += $"Secondary Pool Drop Weight - {secondary:F2}\n";
			if (secondaryWeights != null && secondaryWeights.Count > 0)
			{
				text += "Secondary Pools Weights:\n";
				int num = 0;
				foreach (KeyValuePair<PoetPoolID, float> secondaryWeight in secondaryWeights)
				{
					text += $"{secondaryWeight.Key.ToString()} - {secondaryWeight.Value:0:##}";
					num++;
					if (num < secondaryWeights.Count - 1)
					{
						text += "\n";
					}
				}
			}
			return text;
		}

		private string GetBannedCardsInfo()
		{
			if (BannedCards.Count == 0)
			{
				return "Banned Cards: None";
			}
			string text = string.Empty;
			if (BannedCards.Count > 0)
			{
				text += "Banned Cards:\n";
				foreach (KeyValuePair<PoetPoolID, List<CardData>> bannedCard in BannedCards)
				{
					text = text + "Pool " + bannedCard.Key.ToString() + ":\n";
					for (int i = 0; i < bannedCard.Value.Count; i++)
					{
						CardData cardData = bannedCard.Value[i];
						text += string.Format("{0} / ID: {1} ({2})", cardData.GetTitle(), cardData.ID, (cardData is WeaponData) ? "Weapon" : "Equipment");
						if (i < bannedCard.Value.Count - 1)
						{
							text += "\n";
						}
					}
				}
			}
			return text;
		}
	}
}
