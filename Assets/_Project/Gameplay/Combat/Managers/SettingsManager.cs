using System;
using System.Collections.Generic;
using AstralShift.ProfileData;
using AstralShift.Rendering;
using Cysharp.Threading.Tasks;
using FMOD.Studio;
using FMODUnity;
using Unity.Mathematics;
using UnityEngine;

public class SettingsManager : MonoBehaviour
{
	public enum Toggle
	{
		NO = 0,
		YES = 1
	}

	public enum Level
	{
		LOW = 0,
		MEDIUM = 1,
		HIGH = 2
	}

	public enum ScreenModeType
	{
		WINDOWED = 0,
		BORDERLESS = 1
	}

	public enum VALocale
	{
		ENG = 0,
		JPN = 1
	}

	public enum Language
	{
		en = 0,
		pt = 1,
		br = 2,
		es = 3,
		ja = 4,
		kr = 5,
		cnsi = 6,
		cntr = 7
	}

	[BankRef]
	public List<string> VABanks;

	[Range(0f, 1f)]
	public float UIopacity = 1f;

	[Range(0f, 1f)]
	public float AttackOpacity = 1f;

	[Range(0f, 1f)]
	public float MasterVolume = 1f;

	[Range(0f, 1f)]
	public float MusicVolume = 1f;

	[Range(0f, 1f)]
	public float SFXVolume = 1f;

	[Range(0f, 1f)]
	public float VoiceVolume = 1f;

	[Range(0f, 1f)]
	public float AmbienceVolume = 1f;

	[SerializeField]
	[Range(0f, 10f)]
	public float ColorBlindStrength = 10f;

	[NonSerialized]
	public VALocale currentVAlocale;

	private string currentVABank = "none";

	private string _masterVCAKey = "Master";

	private string _musicVCAKey = "Music";

	private string _sfxVCAKey = "SFX";

	private string _voiceVCAKey = "Voice";

	private string _ambienceVCAKey = "Ambience";

	private VCA Master_VCA;

	private VCA Music_VCA;

	private VCA SFX_VCA;

	private VCA Voice_VCA;

	private VCA Ambience_VCA;

	private string ScreenID;

	private ScreenSettingsHelper _screenSettingsHelper;

	private int _colorBlindIdx;

	public Action OnRefresh;

	public Action OnSettingsSaved;

	public Action OnSettingsRolledBack;

	public Action<float> OnTextSpeedChange;

	public Action<float> OnAutoWaitSpeedChange;

	public int LanguageIdx;

	public int VALangIdx;

	public int TextSpeed;

	public int TextWaitSpeed;

	public bool ForceSkip;

	public bool AutoAim;

	public bool UltiSkip;

	public bool DamageNumbers = true;

	public bool HealthBar = true;

	public int VAOnOff;

	public int ScreenIndex;

	public int Resolution;

	public int ScreenMode;

	public int Quality;

	public int VSync;

	public int HDR;

	public float Brightness = 5f;

	public float Gamma = 5f;

	public float Contrast = 5f;

	public int FpsLimit = 3;

	public float BloomIntensity;

	public ASPostProcessingPass.ColorBlindModeEnum ColorBlindMode;

	public bool CameraShake = true;

	public float CursorScale = 5f;

	public float CursorHue;

	public float CursorSaturation;

	private int[] _frameCaps = new int[10] { -1, 30, 40, 60, 75, 120, 144, 180, 240, 360 };

	private float[] _textSpeeds = new float[3] { 0.1f, 0.04f, 0.02f };

	private float[] _textWaitSpeeds = new float[3] { 2.5f, 5f, 7.5f };

	private float[] _filterLevels = new float[3] { 0.33f, 0.66f, 1f };

	public event Action<bool> OnAutoAimChange;

	public event Action<bool> OnHealthBarToggle;

	public event Action<float> OnBloomChange;

	public event Action<float> OnCursorScaleChange;

	public event Action<float, float> OnCursorColorChange;

	public event Action OnResolutionChanged;

	public void Setup()
	{
		SettingsData.OnAudioReset += HandleAudioReset;
		SettingsData.OnGraphicsReset += HandleGraphicsReset;
		SettingsData.OnControlsReset += HandleControlsReset;
		SettingsData.OnAccessibilityReset += HandleAccessibilityReset;
		SettingsData.OnGeneralReset += HandleGeneralReset;
		Master_VCA = RuntimeManager.GetVCA("vca:/" + _masterVCAKey);
		Music_VCA = RuntimeManager.GetVCA("vca:/" + _musicVCAKey);
		SFX_VCA = RuntimeManager.GetVCA("vca:/" + _sfxVCAKey);
		Voice_VCA = RuntimeManager.GetVCA("vca:/" + _voiceVCAKey);
		Ambience_VCA = RuntimeManager.GetVCA("vca:/" + _ambienceVCAKey);
		_screenSettingsHelper = base.gameObject.AddComponent<ScreenSettingsHelper>();
	}

	private void OnDestroy()
	{
		SettingsData.OnAudioReset -= HandleAudioReset;
		SettingsData.OnGraphicsReset -= HandleGraphicsReset;
		SettingsData.OnControlsReset -= HandleControlsReset;
		SettingsData.OnAccessibilityReset -= HandleAccessibilityReset;
		SettingsData.OnGeneralReset -= HandleGeneralReset;
	}

	private void HandleAudioReset()
	{
		LoadAudioSettings();
		Refresh();
	}

	private void HandleGraphicsReset()
	{
		LoadGraphicsSettings();
		Refresh();
	}

	private void HandleControlsReset()
	{
		Refresh();
	}

	private void HandleAccessibilityReset()
	{
		LoadAccessibilitySettings();
		Refresh();
	}

	private void HandleGeneralReset()
	{
		LoadGeneralSettings();
		Refresh();
	}

	public void SetAutoAim(bool state)
	{
		AutoAim = state;
		this.OnAutoAimChange?.Invoke(state);
	}

	public void SetUltiSkip(bool state)
	{
		UltiSkip = state;
	}

	public void SetDamageNumbers(bool state)
	{
		DamageNumbers = state;
	}

	public void SetHealthBar(bool state)
	{
		HealthBar = state;
		this.OnHealthBarToggle?.Invoke(state);
	}

	public void SetUIOpacity(float opacity)
	{
	}

	public void SetAttackOpacity(float opacity)
	{
	}

	public void SetTextSpeed(int index)
	{
		TextSpeed = index;
		OnTextSpeedChange?.Invoke(_textSpeeds[TextSpeed]);
	}

	public float GetTextSpeed()
	{
		return _textSpeeds[TextSpeed];
	}

	public void SetTextWaitSpeed(int index)
	{
		TextWaitSpeed = index;
		OnAutoWaitSpeedChange?.Invoke(_textWaitSpeeds[TextWaitSpeed]);
	}

	public float GetTextWaitSpeed()
	{
		return _textWaitSpeeds[TextWaitSpeed];
	}

	public void SetLanguage(int index)
	{
		LanguageIdx = index;
		Language language = (Language)index;
		LocalizationMediator.SetLanguage(language.ToString());
	}

	public void SetMasterVolume(float volume)
	{
		MasterVolume = volume;
		Master_VCA.setVolume(MasterVolume);
	}

	public void SetMusicVolume(float volume)
	{
		MusicVolume = volume;
		Music_VCA.setVolume(MusicVolume);
	}

	public void SetSFXVolume(float volume)
	{
		SFXVolume = volume;
		SFX_VCA.setVolume(SFXVolume);
	}

	public void SetVoiceVolume(float volume)
	{
		VoiceVolume = volume;
		Voice_VCA.setVolume(VoiceVolume);
	}

	public void SetAmbienceVolume(float volume)
	{
		AmbienceVolume = volume;
		Ambience_VCA.setVolume(AmbienceVolume);
	}

	public void SetVA(int state)
	{
		VAOnOff = state;
	}

	public bool IsVAOn()
	{
		return VAOnOff == 1;
	}

	public void SetVALanguage(int index)
	{
		VALangIdx = index;
		currentVAlocale = (VALocale)VALangIdx;
		SwapBank(currentVAlocale.ToString());
	}

	private void SwapBank(string locale)
	{
		if (currentVABank != "none")
		{
			UnloadBank(currentVABank);
		}
		string bankName = VABanks.Find((string s) => s.EndsWith(locale));
		try
		{
			RuntimeManager.LoadBank(bankName);
			currentVABank = bankName;
		}
		catch (BankLoadException exception)
		{
			Debug.LogException(exception);
		}
		RuntimeManager.WaitForAllSampleLoading();
	}

	private void UnloadBank(string bank)
	{
		RuntimeManager.UnloadBank(bank);
		currentVABank = "none";
	}

	private void LoadBank(int idx)
	{
		for (int i = 0; i < VABanks.Count; i++)
		{
			UnloadBank(VABanks[i]);
		}
		SetVALanguage(idx);
	}

	public void SetBrightness(float value)
	{
		Brightness = value;
		ASPostProcessingPass.SetBrightness(math.remap(0f, 10f, 0f, 2f, Brightness));
	}

	public void SetGamma(float value)
	{
		Gamma = value;
		ASPostProcessingPass.SetGamma(math.remap(0f, 10f, 0f, 2f, Gamma));
	}

	public void SetContrast(float value)
	{
		Contrast = value;
		float contrast = ((!(Contrast >= 5f)) ? math.remap(0f, 5f, 0.5f, 1f, Contrast) : math.remap(5f, 10f, 1f, 2f, Contrast));
		ASPostProcessingPass.SetContrast(contrast);
	}

	public void SetVsync(int index)
	{
		VSync = index;
		_screenSettingsHelper.SetVsync(index == 1);
	}

	public void SetHDR(int index)
	{
		HDR = index;
	}

	public void SetFramerateLimit(int index)
	{
		FpsLimit = index;
		Application.targetFrameRate = _frameCaps[FpsLimit];
	}

	public void ChangeBloom(float intensity)
	{
		BloomIntensity = intensity;
		this.OnBloomChange?.Invoke(intensity);
	}

	private void SetupResolution()
	{
		SetScreenMode(ScreenMode);
		_screenSettingsHelper.GetAvailableResolutions();
		string text = Screen.mainWindowDisplayInfo.name;
		if (string.IsNullOrEmpty(ScreenID) || ScreenID != text)
		{
			SetDefaultScreenAndResolution();
		}
		else
		{
			SetResolution(Resolution);
		}
	}

	private void SetDefaultScreenAndResolution()
	{
		ScreenID = Screen.mainWindowDisplayInfo.name;
		SettingsData.Instance.ScreenID = ScreenID;
		ScreenIndex = 0;
		SettingsData.Instance.ScreenIndex = ScreenIndex;
		SetResolution(0);
	}

	public Resolution[] GetResolutions()
	{
		return _screenSettingsHelper.GetAvailableResolutions();
	}

	public List<DisplayInfo> GetScreens()
	{
		return _screenSettingsHelper.GetAvailableDisplays();
	}

	public int[] GetFrameCaps()
	{
		return _frameCaps;
	}

	public async UniTask SetDisplay(int index)
	{
		ScreenIndex = index;
		SettingsData.Instance.ScreenIndex = ScreenIndex;
		ScreenID = GetScreens()[ScreenIndex].name;
		SettingsData.Instance.ScreenID = ScreenID;
		await _screenSettingsHelper.ChangeDisplay(ScreenIndex);
	}

	private void SetResolution(int index)
	{
		_screenSettingsHelper.ChangeResolution(index);
		Resolution = index;
		this.OnResolutionChanged?.Invoke();
	}

	private void SetScreenMode(int index)
	{
		ScreenMode = index;
		Screen.fullScreenMode = ScreenSettingsHelper.FullScreenModes[index];
	}

	private void SetGraphicsQuality(int index, bool isTemp = false)
	{
		Quality = index;
	}

	public void ApplyGraphicsSettings(int resolution, int screenMode, int quality)
	{
		SetGraphicsQuality(quality);
		SetScreenMode(screenMode);
		Resolution[] availableResolutions = _screenSettingsHelper.GetAvailableResolutions();
		if (Resolution >= availableResolutions.Length || Resolution < 0)
		{
			SetDefaultScreenAndResolution();
			ProfileDataManager.SaveConfigs();
		}
		else
		{
			SetResolution(resolution);
		}
	}

	public void SetColorBlindMode(int idx)
	{
		_colorBlindIdx = idx;
		ColorBlindMode = (ASPostProcessingPass.ColorBlindModeEnum)idx;
		ASPostProcessingPass.SetColorBlindMode(ColorBlindMode, ColorBlindStrength);
	}

	public void SetColorBlindStrength(float value)
	{
		ColorBlindStrength = value;
		ASPostProcessingPass.SetColorBlindMode(ColorBlindMode, ColorBlindStrength);
	}

	public void SetCameraShake(bool state)
	{
		CameraShake = state;
	}

	public void SetCursorScale(float scale)
	{
		CursorScale = scale;
		this.OnCursorScaleChange?.Invoke(scale);
	}

	public void SetCursorColor(float hue, float saturation)
	{
		CursorHue = hue;
		CursorSaturation = saturation;
		this.OnCursorColorChange?.Invoke(hue, saturation);
	}

	public void SaveSettings()
	{
		SettingsData.Instance.AutoAim = AutoAim;
		SettingsData.Instance.UltiSkip = UltiSkip;
		SettingsData.Instance.DamageNumbers = DamageNumbers;
		SettingsData.Instance.HealthBar = HealthBar;
		SettingsData.Instance.UIOpacity = UIopacity;
		SettingsData.Instance.AtkOpacity = AttackOpacity;
		SettingsData.Instance.BloomIntensity = BloomIntensity;
		SettingsData.Instance.FPSLimit = FpsLimit;
		SettingsData.Instance.Contrast = Contrast;
		SettingsData.Instance.Gamma = Gamma;
		SettingsData.Instance.Brightness = Brightness;
		SettingsData.Instance.Quality = Quality;
		SettingsData.Instance.VSync = VSync;
		SettingsData.Instance.Resolution = Resolution;
		SettingsData.Instance.ScreenID = ScreenID;
		SettingsData.Instance.ScreenIndex = ScreenIndex;
		SettingsData.Instance.Quality = Quality;
		SettingsData.Instance.ScreenMode = ScreenMode;
		SettingsData.Instance.Language = LanguageIdx;
		SettingsData.Instance.VAOnOff = VAOnOff;
		SettingsData.Instance.VALangIdx = VALangIdx;
		SettingsData.Instance.MasterVolume = MasterVolume;
		SettingsData.Instance.MusicVolume = MusicVolume;
		SettingsData.Instance.SFXVolume = SFXVolume;
		SettingsData.Instance.VoiceVolume = VoiceVolume;
		SettingsData.Instance.AmbienceVolume = AmbienceVolume;
		SettingsData.Instance.ColorblindIdx = (int)ColorBlindMode;
		SettingsData.Instance.CameraShake = CameraShake;
		SettingsData.Instance.CursorScale = CursorScale;
		SettingsData.Instance.CursorHue = CursorHue;
		SettingsData.Instance.CursorSaturation = CursorSaturation;
		SettingsData.Instance.TextSpeed = TextSpeed;
		SettingsData.Instance.TextWaitSpeed = TextWaitSpeed;
		SettingsData.Instance.ForceSkip = ForceSkip;
		ProfileDataManager.SaveConfigs();
	}

	public void LoadSettings()
	{
		try
		{
			LoadGeneralSettings();
			LoadAudioSettings();
			LoadGraphicsSettings();
			LoadAccessibilitySettings();
			LoadBank(VALangIdx);
		}
		catch (Exception)
		{
			SettingsData.Instance = new SettingsData();
			LoadSettings();
		}
	}

	private void LoadGeneralSettings()
	{
		AutoAim = SettingsData.Instance.AutoAim;
		UltiSkip = SettingsData.Instance.UltiSkip;
		HealthBar = SettingsData.Instance.HealthBar;
		DamageNumbers = SettingsData.Instance.DamageNumbers;
		UIopacity = SettingsData.Instance.UIOpacity;
		AttackOpacity = SettingsData.Instance.AtkOpacity;
		TextSpeed = SettingsData.Instance.TextSpeed;
		TextWaitSpeed = SettingsData.Instance.TextWaitSpeed;
		ForceSkip = SettingsData.Instance.ForceSkip;
		LanguageIdx = SettingsData.Instance.Language;
		SetAutoAim(AutoAim);
		SetUltiSkip(UltiSkip);
		SetHealthBar(HealthBar);
		SetDamageNumbers(DamageNumbers);
		SetUIOpacity(UIopacity);
		SetAttackOpacity(AttackOpacity);
		SetTextSpeed(TextSpeed);
		SetTextWaitSpeed(TextWaitSpeed);
		SetLanguage(LanguageIdx);
	}

	private void LoadAudioSettings()
	{
		MasterVolume = SettingsData.Instance.MasterVolume;
		MusicVolume = SettingsData.Instance.MusicVolume;
		SFXVolume = SettingsData.Instance.SFXVolume;
		VoiceVolume = SettingsData.Instance.VoiceVolume;
		AmbienceVolume = SettingsData.Instance.AmbienceVolume;
		VAOnOff = SettingsData.Instance.VAOnOff;
		VALangIdx = SettingsData.Instance.VALangIdx;
		SetMasterVolume(MasterVolume);
		SetMusicVolume(MusicVolume);
		SetSFXVolume(SFXVolume);
		SetAmbienceVolume(AmbienceVolume);
		SetVoiceVolume(VoiceVolume);
		SetVA(VAOnOff);
		SetVALanguage(VALangIdx);
	}

	private void LoadGraphicsSettings()
	{
		BloomIntensity = SettingsData.Instance.BloomIntensity;
		FpsLimit = SettingsData.Instance.FPSLimit;
		Brightness = SettingsData.Instance.Brightness;
		Gamma = SettingsData.Instance.Gamma;
		Contrast = SettingsData.Instance.Contrast;
		Resolution = SettingsData.Instance.Resolution;
		VSync = SettingsData.Instance.VSync;
		ScreenID = SettingsData.Instance.ScreenID;
		ScreenIndex = SettingsData.Instance.ScreenIndex;
		Quality = SettingsData.Instance.Quality;
		ScreenMode = ((SettingsData.Instance.ScreenMode > Enum.GetNames(typeof(ScreenModeType)).Length - 1) ? 1 : SettingsData.Instance.ScreenMode);
		SetBrightness(Brightness);
		SetGamma(Gamma);
		SetContrast(Contrast);
		SetupResolution();
		SetFramerateLimit(FpsLimit);
		SetVsync(VSync);
		SetGraphicsQuality(Quality);
		this.OnBloomChange?.Invoke(BloomIntensity);
	}

	private void LoadAccessibilitySettings()
	{
		_colorBlindIdx = SettingsData.Instance.ColorblindIdx;
		ColorBlindMode = (ASPostProcessingPass.ColorBlindModeEnum)_colorBlindIdx;
		ColorBlindStrength = SettingsData.Instance.ColorblindStrength;
		CameraShake = SettingsData.Instance.CameraShake;
		CursorScale = SettingsData.Instance.CursorScale;
		CursorHue = SettingsData.Instance.CursorHue;
		CursorSaturation = SettingsData.Instance.CursorSaturation;
		SetColorBlindMode(_colorBlindIdx);
		SetColorBlindStrength(ColorBlindStrength);
		SetCameraShake(CameraShake);
		SetCursorScale(CursorScale);
		SetCursorColor(CursorHue, CursorSaturation);
	}

	internal void Refresh()
	{
		OnRefresh?.Invoke();
	}

	internal void SettingsRolledBack()
	{
		OnSettingsRolledBack?.Invoke();
	}

	internal void SettingsSaved()
	{
		OnSettingsSaved?.Invoke();
	}
}
