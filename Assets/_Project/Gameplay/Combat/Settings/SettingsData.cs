using System;
using Newtonsoft.Json;

public class SettingsData
{
	private static SettingsData instance;

	public int Version = 1;

	public float MasterVolume = 1f;

	public float MusicVolume = 1f;

	public float SFXVolume = 1f;

	public float VoiceVolume = 1f;

	public float AmbienceVolume = 1f;

	public int VAOnOff;

	public int VALangIdx;

	public float Brightness;

	public float Gamma;

	public float Contrast;

	public int FPSLimit;

	public float BloomIntensity;

	public string ScreenID;

	public int ScreenIndex;

	public int Resolution;

	public int ScreenMode;

	public int Quality;

	public int VSync;

	public string PlayerProfile;

	public int ColorblindIdx;

	public float ColorblindStrength;

	public bool CameraShake;

	public float CursorScale;

	public float CursorHue;

	public float CursorSaturation;

	public int Language;

	public int TextSpeed;

	public int TextWaitSpeed;

	public bool ForceSkip;

	public bool AutoAim;

	public bool UltiSkip;

	public bool HealthBar;

	public bool DamageNumbers;

	public float UIOpacity;

	public float AtkOpacity;

	public static SettingsData Instance
	{
		get
		{
			if (instance == null)
			{
				instance = new SettingsData();
			}
			return instance;
		}
		set
		{
			instance = value;
		}
	}

	[JsonProperty]
	public bool SettingsHaveBeenSet { get; set; }

	public static event Action OnAudioReset;

	public static event Action OnGraphicsReset;

	public static event Action OnControlsReset;

	public static event Action OnAccessibilityReset;

	public static event Action OnGeneralReset;

	public SettingsData()
	{
		ResetAll();
	}

	public void ResetAll()
	{
		ResetAudio();
		ResetGraphics();
		ResetControls();
		ResetAccessibility();
		ResetGeneral();
		SettingsHaveBeenSet = false;
	}

	public void ResetAudio()
	{
		MasterVolume = 1f;
		MusicVolume = 1f;
		SFXVolume = 1f;
		VoiceVolume = 1f;
		AmbienceVolume = 1f;
		VAOnOff = 1;
		VALangIdx = 0;
		SettingsData.OnAudioReset?.Invoke();
	}

	public void ResetGraphics()
	{
		Brightness = 5f;
		Gamma = 5f;
		Contrast = 5f;
		FPSLimit = 3;
		BloomIntensity = 1f;
		ScreenID = "";
		ScreenIndex = 0;
		Resolution = 0;
		ScreenMode = 1;
		Quality = 0;
		VSync = 0;
		SettingsData.OnGraphicsReset?.Invoke();
	}

	public void ResetControls()
	{
		PlayerProfile = "Modern";
		SettingsData.OnControlsReset?.Invoke();
	}

	public void ResetAccessibility()
	{
		ColorblindIdx = 0;
		ColorblindStrength = 5f;
		CameraShake = true;
		CursorScale = 5f;
		CursorHue = 0f;
		CursorSaturation = 0f;
		SettingsData.OnAccessibilityReset?.Invoke();
	}

	public void ResetGeneral()
	{
		Language = 0;
		TextSpeed = 1;
		TextWaitSpeed = 1;
		ForceSkip = false;
		AutoAim = false;
		UltiSkip = false;
		DamageNumbers = true;
		HealthBar = true;
		UIOpacity = 100f;
		AtkOpacity = 100f;
		SettingsData.OnGeneralReset?.Invoke();
	}
}
