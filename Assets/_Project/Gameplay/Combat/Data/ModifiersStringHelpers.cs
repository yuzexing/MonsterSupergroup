using System.Collections.Generic;
using AstralShift.HellMaiden.Data.Perks;
using TMPro;

namespace AstralShift.HellMaiden.Data
{
	public class ModifiersStringHelpers
	{
		private static EquipmentVisualsTemplateLUT _equipmentVisualsTemplateLUT;

		private static PerkTemplateLUT _perkTemplateLUT;

		public static string GetEquipmentModifierStringIcon(uint modifierID)
		{
			if (!_equipmentVisualsTemplateLUT)
			{
				_equipmentVisualsTemplateLUT = GameDirector.Instance.runtimeDB.EquipmentDB.VisualsTemplateLUT;
			}
			return GetSpriteAssetFormatedString(_equipmentVisualsTemplateLUT.GetModifierIconTMPSpriteAsset(modifierID));
		}

		public static string GetEquipmentModifierStringIcon(EquipmentModifierID modifierID)
		{
			if (!_equipmentVisualsTemplateLUT)
			{
				_equipmentVisualsTemplateLUT = GameDirector.Instance.runtimeDB.EquipmentDB.VisualsTemplateLUT;
			}
			return GetSpriteAssetFormatedString(_equipmentVisualsTemplateLUT.GetModifierIconTMPSpriteAsset(modifierID));
		}

		public static string GetEquipmentModifierNameLocKey(uint modifierID)
		{
			if (!_equipmentVisualsTemplateLUT)
			{
				_equipmentVisualsTemplateLUT = GameDirector.Instance.runtimeDB.EquipmentDB.VisualsTemplateLUT;
			}
			return _equipmentVisualsTemplateLUT.GetModifierNameLocKey(modifierID);
		}

		public static string GetEquipmentModifierNameLocKey(EquipmentModifierID modifierID)
		{
			if (!_equipmentVisualsTemplateLUT)
			{
				_equipmentVisualsTemplateLUT = GameDirector.Instance.runtimeDB.EquipmentDB.VisualsTemplateLUT;
			}
			return _equipmentVisualsTemplateLUT.GetModifierNameLocKey(modifierID);
		}

		public static List<string> GetAllEquipmentModifiersNamesLocKeys()
		{
			if (!_equipmentVisualsTemplateLUT)
			{
				_equipmentVisualsTemplateLUT = GameDirector.Instance.runtimeDB.EquipmentDB.VisualsTemplateLUT;
			}
			return _equipmentVisualsTemplateLUT.GetModifiersNamesLocKeys();
		}

		public static string GetPerkModifierStringIcon(uint modifierID)
		{
			if (!_perkTemplateLUT)
			{
				_perkTemplateLUT = GameDirector.Instance.runtimeDB.PerkDB.VisualsTemplateLUT;
			}
			return GetSpriteAssetFormatedString(_perkTemplateLUT.GetModifierTMPSpriteAsset(modifierID));
		}

		public static string GetPerkModifierStringIcon(PerkModifierID modifierID)
		{
			if (!_perkTemplateLUT)
			{
				_perkTemplateLUT = GameDirector.Instance.runtimeDB.PerkDB.VisualsTemplateLUT;
			}
			return GetSpriteAssetFormatedString(_perkTemplateLUT.GetModifierIconTMPSpriteAsset(modifierID));
		}

		public static string GetPerkModifierNameLocKey(uint modifierID)
		{
			if (!_perkTemplateLUT)
			{
				_perkTemplateLUT = GameDirector.Instance.runtimeDB.PerkDB.VisualsTemplateLUT;
			}
			return _perkTemplateLUT.GetModifierNameLocKey(modifierID);
		}

		public static string GetPerkModifierNameLocKey(PerkModifierID modifierID)
		{
			if (!_perkTemplateLUT)
			{
				_perkTemplateLUT = GameDirector.Instance.runtimeDB.PerkDB.VisualsTemplateLUT;
			}
			return _perkTemplateLUT.GetModifierNameLocKey(modifierID);
		}

		public static List<string> GetAllPerkModifiersNamesLocKeys()
		{
			if (!_perkTemplateLUT)
			{
				_perkTemplateLUT = GameDirector.Instance.runtimeDB.PerkDB.VisualsTemplateLUT;
			}
			return _perkTemplateLUT.GetModifiersNamesLocKeys();
		}

		public static string GetSpriteAssetFormatedString(TMP_SpriteAsset fontAsset)
		{
			return GetSpriteAssetFormatedString(fontAsset, 0);
		}

		public static string GetSpriteAssetFormatedString(TMP_SpriteAsset fontAsset, int index)
		{
			return $"<sprite=\"{fontAsset.name}\" index={index}>";
		}
	}
}
