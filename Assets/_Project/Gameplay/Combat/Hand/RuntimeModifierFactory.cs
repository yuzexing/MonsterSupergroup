using System;
using System.Reflection;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Data.Perks;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public class RuntimeModifierFactory
	{
		private static RuntimeModifierFactory instance;

		public static RuntimeModifierFactory Instance
		{
			get
			{
				if (instance == null)
				{
					instance = new RuntimeModifierFactory();
				}
				return instance;
			}
		}

		public RuntimeEquipmentModifier[] GetRuntimeModifiersFromEquipmentData(EquipmentData equipmentData, uint levelIndex)
		{
			if (equipmentData.Levels == null || equipmentData.Levels.Length <= levelIndex || equipmentData.Levels[levelIndex] == null)
			{
				return null;
			}
			EquipmentLevelModifiersData equipmentLevelModifiersData = equipmentData.Levels[levelIndex];
			int num = equipmentLevelModifiersData.Modifiers.Length;
			RuntimeEquipmentModifier[] array = new RuntimeEquipmentModifier[num];
			for (int i = 0; i < num; i++)
			{
				array[i] = GetRuntimeModifierFromEquipmentData(equipmentLevelModifiersData.Modifiers[i]);
				if (array[i] == null)
				{
					return null;
				}
			}
			return array;
		}

		private RuntimeEquipmentModifier GetRuntimeModifierFromEquipmentData(EquipmentDataModifier dataModifier)
		{
			DataModifierResolver.BuildCache();
			EquipmentModifierID modifierID = dataModifier.ModifierID;
			if (!DataModifierResolver.TryGetEquipmentModifierClassTypeByID(modifierID, out var type))
			{
				Debug.LogError($"RuntimeModifierFactory: Unknown Equipment Modifier ID: {modifierID}");
				return null;
			}
			RuntimeEquipmentModifier runtimeEquipmentModifier = (RuntimeEquipmentModifier)Activator.CreateInstance(type);
			if (runtimeEquipmentModifier == null)
			{
				return null;
			}
			if (!DataModifierResolver.TryGetEquipmentParamsClassTypeByID(modifierID, out var paramType) || paramType == null)
			{
				return runtimeEquipmentModifier;
			}
			object parameters = dataModifier.Parameters;
			if (parameters == null)
			{
				return runtimeEquipmentModifier;
			}
			object obj = Activator.CreateInstance(paramType);
			DataModifierUtils.CopyModifierParams(parameters, obj);
			FieldInfo fieldInfo = DataModifierResolver.EquipmentModifierParamsInstanceFieldById[modifierID];
			if (fieldInfo == null)
			{
				Debug.LogError("RuntimeModifierFactory: Modifier " + type.Name + " has no field marked with [InjectEquipmentModifierParams]");
				return runtimeEquipmentModifier;
			}
			fieldInfo.SetValue(runtimeEquipmentModifier, obj);
			runtimeEquipmentModifier.ID = modifierID;
			runtimeEquipmentModifier.HasMultiSlotConfig = dataModifier.HasMultiSlotConfig;
			runtimeEquipmentModifier.IsSelfApplied = dataModifier.MultiSlot.IsSelfApplied;
			runtimeEquipmentModifier.LeftSlots = dataModifier.MultiSlot.LeftSlots;
			runtimeEquipmentModifier.RightSlots = dataModifier.MultiSlot.RightSlots;
			return runtimeEquipmentModifier;
		}

		public RuntimePerkModifier[] GetRuntimeModifiersFromPerkData(PerkData perkData, PerkRarity rarity)
		{
			PerkRarityModifiersData[] allRarities = perkData.GetAllRarities();
			if (allRarities == null || allRarities.Length == 0)
			{
				return null;
			}
			if (!perkData.HasRarity(rarity))
			{
				return null;
			}
			PerkRarityModifiersData rarity2 = perkData.GetRarity(rarity);
			int num = rarity2.Modifiers.Length;
			RuntimePerkModifier[] array = new RuntimePerkModifier[num];
			for (int i = 0; i < num; i++)
			{
				array[i] = GetRuntimeModifierFromPerkData(rarity2.Modifiers[i]);
				if (array[i] == null)
				{
					return null;
				}
			}
			return array;
		}

		public RuntimePerkModifier GetRuntimeModifierFromPerkData(PerkDataModifier dataModifier)
		{
			DataModifierResolver.BuildCache();
			PerkModifierID modifierID = dataModifier.ModifierID;
			if (!DataModifierResolver.TryGetPerkModifierClassTypeByID(modifierID, out var type))
			{
				Debug.LogError($"RuntimeModifierFactory: Unknown Perk Modifier ID: {modifierID}");
				return null;
			}
			RuntimePerkModifier runtimePerkModifier = (RuntimePerkModifier)Activator.CreateInstance(type);
			if (runtimePerkModifier == null)
			{
				return null;
			}
			if (!DataModifierResolver.TryGetPerkParamsClassTypeByID(modifierID, out var paramType) || paramType == null)
			{
				return runtimePerkModifier;
			}
			object parameters = dataModifier.Parameters;
			if (parameters == null)
			{
				return runtimePerkModifier;
			}
			object obj = Activator.CreateInstance(paramType);
			DataModifierUtils.CopyModifierParams(parameters, obj);
			FieldInfo fieldInfo = DataModifierResolver.PerkModifierParamsInstanceFieldById[modifierID];
			if (fieldInfo == null)
			{
				Debug.LogError("RuntimeModifierFactory: Modifier " + type.Name + " has no field marked with [InjectPerkModifierParams]");
				return runtimePerkModifier;
			}
			fieldInfo.SetValue(runtimePerkModifier, obj);
			runtimePerkModifier.ID = modifierID;
			return runtimePerkModifier;
		}
	}
}
