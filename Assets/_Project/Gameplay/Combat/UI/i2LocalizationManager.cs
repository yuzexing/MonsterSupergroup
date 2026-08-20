using System.Collections.Generic;
using I2.Loc;
using TMPro;
using UnityEngine;

[DefaultExecutionOrder(-2)]
public class i2LocalizationManager : MonoBehaviour
{
	[Header("Global TMP FallBacks Current Text")]
	[SerializeField]
	private TMP_FontAsset fontAtlasLatin;

	[SerializeField]
	private TMP_FontAsset fontAtlasJa;

	[SerializeField]
	private TMP_FontAsset fontAtlasKr;

	[SerializeField]
	private TMP_FontAsset fontAtlasCnsi;

	[SerializeField]
	private TMP_FontAsset fontAtlasCntr;

	[SerializeField]
	private TMP_FontAsset[] fontAtlasSymbols;

	public static TMP_FontAsset latinDefault;

	public static TMP_FontAsset jaTxt;

	public static TMP_FontAsset koTxt;

	public static TMP_FontAsset cnsiTxt;

	public static TMP_FontAsset cntrTxt;

	public static TMP_FontAsset[] symbolsTxt;

	private static List<TMP_FontAsset> globalFonts = new List<TMP_FontAsset>();

	private static TMP_FontAsset CurrentFont;

	private static bool _isDefault = true;

	private void Awake()
	{
		latinDefault = fontAtlasLatin;
		jaTxt = fontAtlasJa;
		koTxt = fontAtlasKr;
		cnsiTxt = fontAtlasCnsi;
		cntrTxt = fontAtlasCntr;
		symbolsTxt = fontAtlasSymbols;
		CurrentFont = latinDefault;
		_isDefault = true;
		globalFonts = TMP_Settings.fallbackFontAssets;
		ApplyForCurrentI2();
	}

	public static void ApplyGlobalFallback(string langCode)
	{
		if (!(TMP_Settings.instance == null))
		{
			TMP_FontAsset tMP_FontAsset = null;
			globalFonts.Clear();
			tMP_FontAsset = langCode switch
			{
				"ja" => jaTxt, 
				"kr" => koTxt, 
				"cnsi" => cnsiTxt, 
				"zh-CN" => cnsiTxt, 
				"cntr" => cntrTxt, 
				"zh-TW" => cntrTxt, 
				_ => latinDefault, 
			};
			_isDefault = tMP_FontAsset == latinDefault;
			if (tMP_FontAsset != null && !_isDefault)
			{
				globalFonts.Add(tMP_FontAsset);
			}
			CurrentFont = tMP_FontAsset;
			globalFonts.AddRange(symbolsTxt);
			TMP_Text[] array = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			for (int i = 0; i < array.Length; i++)
			{
				array[i].ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
			}
		}
	}

	public static void ApplyForCurrentI2()
	{
		ApplyGlobalFallback(LocalizationManager.CurrentLanguageCode);
	}
}
