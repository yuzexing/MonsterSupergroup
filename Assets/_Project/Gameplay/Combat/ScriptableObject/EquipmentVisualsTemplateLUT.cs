using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using TMPro;
using UnityEngine;

[CreateAssetMenu(fileName = "New Equipment Card Template Visual Data LUT", menuName = "HellMaiden/Data/Cards/Visuals/Equipment Card Visuals Template LUT (New)")]
public class EquipmentVisualsTemplateLUT : ScriptableObject
{
	[Serializable]
	public class BehaviourVisualEntry
	{
		public EquipmentModifierID modifierID;

		public TypeAssets assets;
	}

	[Serializable]
	public class TypeAssets
	{
		public Sprite sprite;

		public TMP_SpriteAsset textIcon;

		public string typeName;
	}

	[SerializeField]
	private List<EquipmentTemplateVisualData> values;

	private Dictionary<EquipmentCardType, EquipmentTemplateVisualData> _cardTypeLUT;

	[Header("Modifier Behaviour Icon Lookup")]
	public List<BehaviourVisualEntry> behaviourVisuals = new List<BehaviourVisualEntry>();

	public TypeAssets defaultAssets;

	private static List<string> _modifierNamesLocKeys;

	public Dictionary<EquipmentCardType, EquipmentTemplateVisualData> CardTypeLUT => _cardTypeLUT ?? CreateEquipmentTypeVisualLUT();

	public void RefreshBehaviourLookup()
	{
		DataModifierResolver.BuildCache();
		Type[] equipmentModifierTypes = DataModifierResolver.EquipmentModifierTypes;
		List<BehaviourVisualEntry> list = new List<BehaviourVisualEntry>();
		Type[] array = equipmentModifierTypes;
		foreach (Type type in array)
		{
			string s = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
			uint id = DeterministicHash.Apply(s);
			BehaviourVisualEntry behaviourVisualEntry = behaviourVisuals.Find((BehaviourVisualEntry e) => e.modifierID.Value == id);
			if (behaviourVisualEntry != null)
			{
				list.Add(behaviourVisualEntry);
				continue;
			}
			list.Add(new BehaviourVisualEntry
			{
				modifierID = new EquipmentModifierID(id),
				assets = new TypeAssets()
			});
		}
		behaviourVisuals = list;
	}

	public List<string> GetModifiersNamesLocKeys()
	{
		if (_modifierNamesLocKeys != null)
		{
			return _modifierNamesLocKeys;
		}
		_modifierNamesLocKeys = new List<string>();
		foreach (BehaviourVisualEntry behaviourVisual in behaviourVisuals)
		{
			_modifierNamesLocKeys.Add(behaviourVisual.assets.typeName.Replace(" ", ""));
		}
		return _modifierNamesLocKeys;
	}

	public Sprite GetPreferredModifierIconSprite(EquipmentLevelModifiersData perLevelModifiers)
	{
		if (perLevelModifiers.Modifiers.Length == 0)
		{
			return defaultAssets.sprite;
		}
		return GetModifierIconSprite(perLevelModifiers.Modifiers[0].ModifierID);
	}

	private Sprite GetModifierIconSprite(uint id)
	{
		BehaviourVisualEntry behaviourVisualEntry = behaviourVisuals.Find((BehaviourVisualEntry e) => e.modifierID.Value == id);
		if (behaviourVisualEntry != null && (bool)behaviourVisualEntry.assets.sprite)
		{
			return behaviourVisualEntry.assets.sprite;
		}
		return defaultAssets?.sprite;
	}

	public TMP_SpriteAsset GetModifierIconTMPSpriteAsset(uint id)
	{
		BehaviourVisualEntry behaviourVisualEntry = behaviourVisuals.Find((BehaviourVisualEntry e) => e.modifierID.Value == id);
		if (behaviourVisualEntry != null && (bool)behaviourVisualEntry.assets.textIcon)
		{
			return behaviourVisualEntry.assets.textIcon;
		}
		return defaultAssets?.textIcon;
	}

	public TMP_SpriteAsset GetModifierIconTMPSpriteAsset(EquipmentModifierID id)
	{
		BehaviourVisualEntry behaviourVisualEntry = behaviourVisuals.Find((BehaviourVisualEntry e) => e.modifierID.Value == id.Value);
		if (behaviourVisualEntry != null && (bool)behaviourVisualEntry.assets.textIcon)
		{
			return behaviourVisualEntry.assets.textIcon;
		}
		return defaultAssets?.textIcon;
	}

	public string GetModifierNameLocKey(uint id)
	{
		BehaviourVisualEntry behaviourVisualEntry = behaviourVisuals.Find((BehaviourVisualEntry e) => e.modifierID.Value == id);
		if (behaviourVisualEntry != null && (bool)behaviourVisualEntry.assets.textIcon)
		{
			return behaviourVisualEntry.assets.typeName.Replace(" ", "");
		}
		return defaultAssets?.typeName.Replace(" ", "");
	}

	public string GetModifierNameLocKey(EquipmentModifierID id)
	{
		BehaviourVisualEntry behaviourVisualEntry = behaviourVisuals.Find((BehaviourVisualEntry e) => e.modifierID.Value == id.Value);
		if (behaviourVisualEntry != null && !string.IsNullOrEmpty(behaviourVisualEntry.assets.typeName))
		{
			return behaviourVisualEntry.assets.typeName.Replace(" ", "");
		}
		return defaultAssets?.typeName.Replace(" ", "");
	}

	public string GetModifierDisplayName(EquipmentModifierID id)
	{
		DataModifierResolver.TryGetEquipmentDisplayName(behaviourVisuals.Find((BehaviourVisualEntry e) => e.modifierID.Value == id.Value).modifierID.Value, out var display);
		return display;
	}

	private Dictionary<EquipmentCardType, EquipmentTemplateVisualData> CreateEquipmentTypeVisualLUT()
	{
		_cardTypeLUT = new Dictionary<EquipmentCardType, EquipmentTemplateVisualData>();
		for (int i = 0; i < Enum.GetNames(typeof(EquipmentCardType)).Length; i++)
		{
			_cardTypeLUT.Add((EquipmentCardType)i, values[i]);
		}
		return _cardTypeLUT;
	}
}
