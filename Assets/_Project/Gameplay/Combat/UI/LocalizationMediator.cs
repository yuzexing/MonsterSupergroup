using System.Collections.Generic;
using I2.Loc;
// using PixelCrushers.DialogueSystem;

public static class LocalizationMediator
{
	private static Dictionary<string, string> tabDictionary;

	static LocalizationMediator()
	{
		tabDictionary = new Dictionary<string, string>();
		tabDictionary["CPM"] = "UI";
		tabDictionary["STG"] = "UI";
		tabDictionary["STT"] = "UI";
		tabDictionary["WSM"] = "UI";
		tabDictionary["DLG"] = "UI";
		tabDictionary["GEN"] = "UI";
		tabDictionary["LST"] = "UI";
		tabDictionary["LSM"] = "UI";
		tabDictionary["TPL"] = "UI";
		tabDictionary["END"] = "UI";
		tabDictionary["STP"] = "UI";
		tabDictionary["CRD"] = "UI";
		tabDictionary["MET"] = "UI";
		tabDictionary["QST"] = "Dialogue System";
		tabDictionary["WPN"] = "Weapon_Cards";
		tabDictionary["MOD"] = "Mod_Cards";
		tabDictionary["ULT"] = "Ultimate";
		tabDictionary["PRK"] = "Perks";
		tabDictionary["VID"] = "Video Subs";
		tabDictionary["ACH"] = "Achievements";
	}

	internal static void SetLanguage(string lang)
	{
		if (LocalizationManager.HasLanguage(lang))
		{
			LocalizationManager.CurrentLanguage = lang;
			// DialogueManager.SetLanguage(lang);
			i2LocalizationManager.ApplyGlobalFallback(lang);
		}
	}

	public static bool GetTranslationPath(ref string term)
	{
		if (term.Length < 3)
		{
			return false;
		}
		string key = term.Substring(0, 3);
		if (tabDictionary.TryGetValue(key, out var value))
		{
			term = value + "/" + term;
			return true;
		}
		return false;
	}

	public static void GetTranslation(ref string term, string tabName = "")
	{
		if (string.IsNullOrEmpty(tabName) && GetTranslationPath(ref term))
		{
			term = LocalizationManager.GetTranslation(term);
			return;
		}
		term = tabName + "/" + term;
		term = LocalizationManager.GetTranslation(term);
	}

	public static string GetTranslation(string term)
	{
		if (GetTranslationPath(ref term))
		{
			return LocalizationManager.GetTranslation(term);
		}
		return term;
	}
}
