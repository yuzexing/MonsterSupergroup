using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.UI.Perks;
using TMPro;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Perks
{
	[CreateAssetMenu(fileName = "New Perk Template LUT", menuName = "HellMaiden/Data/Perks/PerkTemplateLUT")]
	public class PerkTemplateLUT : ScriptableObject
	{
		[Serializable]
		public class PerkModifierVisualEntry
		{
			public PerkModifierID modifierID;

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
		private PerkView uiPerkViewTemplate;

		[SerializeField]
		private List<Perk3DView> perRarity3DTemplates;

		[Header("Modifier Icon Lookup")]
		public List<PerkModifierVisualEntry> modifierVisuals = new List<PerkModifierVisualEntry>();

		public TypeAssets defaultAssets;

		private static List<string> _modifierNamesKeys;

		public PerkView UIPerkViewTemplate => uiPerkViewTemplate;

		public List<Perk3DView> PerRarity3DTemplates => perRarity3DTemplates;

		public void RefreshModifierLookup()
		{
			DataModifierResolver.BuildCache();
			Type[] perkModifierTypes = DataModifierResolver.PerkModifierTypes;
			List<PerkModifierVisualEntry> list = new List<PerkModifierVisualEntry>();
			Type[] array = perkModifierTypes;
			foreach (Type type in array)
			{
				string s = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
				uint id = DeterministicHash.Apply(s);
				PerkModifierVisualEntry perkModifierVisualEntry = modifierVisuals.Find((PerkModifierVisualEntry e) => e.modifierID.Value == id);
				if (perkModifierVisualEntry != null)
				{
					list.Add(perkModifierVisualEntry);
					continue;
				}
				list.Add(new PerkModifierVisualEntry
				{
					modifierID = new PerkModifierID(id),
					assets = new TypeAssets()
				});
			}
			modifierVisuals = list;
		}

		public List<string> GetModifiersNamesLocKeys()
		{
			if (_modifierNamesKeys != null)
			{
				return _modifierNamesKeys;
			}
			_modifierNamesKeys = new List<string>();
			foreach (PerkModifierVisualEntry modifierVisual in modifierVisuals)
			{
				_modifierNamesKeys.Add(modifierVisual.assets.typeName.Replace(" ", ""));
			}
			return _modifierNamesKeys;
		}

		public Sprite GetPreferredModifierIconSprite(PerkRarityModifiersData perRarityModifiers)
		{
			if (perRarityModifiers.Modifiers.Length == 0)
			{
				return defaultAssets.sprite;
			}
			return GetModifierIconSprite(
				perRarityModifiers.Modifiers[0].ModifierIdValue);
		}

		public Sprite GetModifierIconSprite(uint id)
		{
			PerkModifierVisualEntry perkModifierVisualEntry = modifierVisuals.Find((PerkModifierVisualEntry e) => e.modifierID.Value == id);
			if (perkModifierVisualEntry != null && (bool)perkModifierVisualEntry.assets.sprite)
			{
				return perkModifierVisualEntry.assets.sprite;
			}
			return defaultAssets?.sprite;
		}

		public TMP_SpriteAsset GetModifierTMPSpriteAsset(uint id)
		{
			PerkModifierVisualEntry perkModifierVisualEntry = modifierVisuals.Find((PerkModifierVisualEntry e) => e.modifierID.Value == id);
			if (perkModifierVisualEntry != null && (bool)perkModifierVisualEntry.assets.textIcon)
			{
				return perkModifierVisualEntry.assets.textIcon;
			}
			return defaultAssets?.textIcon;
		}

		public TMP_SpriteAsset GetModifierIconTMPSpriteAsset(PerkModifierID id)
		{
			PerkModifierVisualEntry perkModifierVisualEntry = modifierVisuals.Find((PerkModifierVisualEntry e) => e.modifierID.Value == id.Value);
			if (perkModifierVisualEntry != null && (bool)perkModifierVisualEntry.assets.textIcon)
			{
				return perkModifierVisualEntry.assets.textIcon;
			}
			return defaultAssets?.textIcon;
		}

		public string GetModifierNameLocKey(uint id)
		{
			PerkModifierVisualEntry perkModifierVisualEntry = modifierVisuals.Find((PerkModifierVisualEntry e) => e.modifierID.Value == id);
			if (perkModifierVisualEntry != null && (bool)perkModifierVisualEntry.assets.textIcon)
			{
				return perkModifierVisualEntry.assets.typeName.Replace(" ", "");
			}
			return defaultAssets?.typeName.Replace(" ", "");
		}

		public string GetModifierNameLocKey(PerkModifierID id)
		{
			PerkModifierVisualEntry perkModifierVisualEntry = modifierVisuals.Find((PerkModifierVisualEntry e) => e.modifierID.Value == id.Value);
			if (perkModifierVisualEntry != null && (bool)perkModifierVisualEntry.assets.textIcon)
			{
				return perkModifierVisualEntry.assets.typeName.Replace(" ", "");
			}
			return defaultAssets?.typeName.Replace(" ", "");
		}

		public string GetModifierDisplayName(PerkModifierID id)
		{
			DataModifierResolver.TryGetPerkDisplayName(modifierVisuals.Find((PerkModifierVisualEntry e) => e.modifierID.Value == id.Value).modifierID.Value, out var display);
			return display;
		}
	}
}
