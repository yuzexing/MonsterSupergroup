using System;
using AstralShift.DebugTools;
using AstralShift.Helpers;
using AstralShift.UI;
using AstralShift.UI.PopupWindows;
using DG.Tweening;
using Rewired;
using UnityEngine;

public class SettingsMenuController : TabMenuController
{
	private struct SettingsSnapshot
	{
		public bool autoAim;

		public bool ultiSkip;

		public bool damageNumbers;

		public bool healthBar;

		public int languageIdx;

		public int textSpeed;

		public int textWaitSpeed;

		public float masterVolume;

		public float musicVolume;

		public float sfxVolume;

		public float voiceVolume;

		public float ambienceVolume;

		public int vaOnOff;

		public int vaLangIdx;

		public int resolution;

		public int screenMode;

		public int quality;

		public int vSync;

		public float brightness;

		public float gamma;

		public float contrast;

		public int fpsLimit;

		public float bloomIntensity;

		public int colorBlindMode;

		public float colorBlindStrength;

		public bool cameraShake;

		public float cameraShakeIntensity;

		public float cursorScale;

		public float cursorHue;

		public float cursorSaturation;
	}

	public SettingsManager settings;

	private Coroutine _dirtySettingsTimer;

	private int _controlsTabIndex;

	private int _generalTabIndex;

	[SerializeField]
	private CustomUnityUIPlayerControllerElementGlyph resetAllButton;

	[SerializeField]
	private CustomUnityUIPlayerControllerElementGlyph resetCurrentButton;

	[SerializeField]
	private float skipHoldTime = 1f;

	[SerializeField]
	private string saveSettingsText = "STG_SaveMessage";

	[SerializeField]
	private string restoreDefaultSettingsText = "STG_ResetAllMessage";

	[SerializeField]
	private string restoreCurrentWindowSettingsText = "STG_ResetCurrentSettingsMessage";

	private SettingMenuControls _settingsMenuControls;

	private SettingMenuGeneral _settingMenuGeneral;

	private Tween _tabMovementTween;

	[SerializeField]
	public UIRectScaleAdjuster[] rectScaleAdjusters;

	private SettingsSnapshot cachedSettings;

	public event Action OnUIButton4Pressed;

	public event Action OnUIButtonSubmitJustPressed;

	public override void Init()
	{
		try
		{
			for (int i = 0; i < tabContents.Length; i++)
			{
				if (tabContents[i] is SettingsTabContentController settingsTabContentController)
				{
					settingsTabContentController.settings = settings;
					settingsTabContentController.mainController = this;
					if (settingsTabContentController is SettingMenuControls settingsMenuControls)
					{
						_controlsTabIndex = i;
						_settingsMenuControls = settingsMenuControls;
					}
					if (settingsTabContentController is SettingMenuGeneral settingMenuGeneral)
					{
						_generalTabIndex = i;
						_settingMenuGeneral = settingMenuGeneral;
					}
				}
			}
		}
		catch (InvalidCastException ex)
		{
			DBL.Log(DBL.Module.Settings, "Failed to cast tab content to SettingMenuControls: " + ex.Message, 2);
		}
		catch (Exception ex2)
		{
			DBL.Log(DBL.Module.Settings, "Error initializing tab contents: " + ex2.Message, 2);
		}
		resetAllButton.SetHold(skipHoldTime);
		resetCurrentButton.SetHold(skipHoldTime);
		base.Init();
		EnableGameObject(state: false);
		settings.OnResolutionChanged += UpdateScaleAdjusters;
	}

	protected override void OnOpeningFinished()
	{
		base.OnOpeningFinished();
		tabSelector.SelectIntroTab();
	}

	protected override void OnControllerTypeChanged()
	{
		if (_currentMenu != null)
		{
			currentSelectable = _currentMenu.currentSelected;
			base.OnControllerTypeChanged();
		}
	}

	public void GoToGeneral()
	{
		SelectTab(_generalTabIndex);
	}

	public void GoToControls()
	{
		SelectTab(_controlsTabIndex);
	}

	public override void UISubmit(InputActionEventData data)
	{
		if (data.eventType == InputActionEventType.ButtonJustPressed)
		{
			this.OnUIButtonSubmitJustPressed?.Invoke();
		}
	}

	public override void UIButton4(InputActionEventData data)
	{
		base.UIButton4(data);
		if (data.eventType == InputActionEventType.ButtonJustPressed && _currentMenu is SettingMenuGraphics { IsDirty: not false } settingMenuGraphics)
		{
			settingMenuGraphics.TryLaunchDirtySettingsPopup();
			return;
		}
		if (data.eventType == InputActionEventType.ButtonJustReleased)
		{
			TimerHoldInteractionTaskHelper.CancelAndDispose();
		}
		if (data.eventType == InputActionEventType.ButtonPressed)
		{
			this.OnUIButton4Pressed?.Invoke();
			TimerHoldInteractionTaskHelper.ProcessHoldAsync(skipHoldTime, LaunchRestoreDefaultSettingsPopup);
		}
	}

	public override void UIButton3(InputActionEventData data)
	{
		base.UIButton3(data);
		if (data.eventType == InputActionEventType.ButtonJustReleased)
		{
			TimerHoldInteractionTaskHelper.CancelAndDispose();
		}
		if (data.eventType == InputActionEventType.ButtonPressed)
		{
			TimerHoldInteractionTaskHelper.ProcessHoldAsync(skipHoldTime, LaunchRestoreCurrentDefaultSettingsPopup);
		}
	}

	public override void Button2(InputActionEventData data)
	{
		if (base.IsActive)
		{
			base.Button2(data);
			CloseMenu();
		}
	}

	public override void UICancelPressed(InputActionEventData data)
	{
		if (base.IsActive)
		{
			base.UICancelPressed(data);
			if (HaveSettingsChanged())
			{
				LaunchSaveSettingsPopup();
			}
			else
			{
				CloseMenu();
			}
		}
	}

	private void LaunchSaveSettingsPopup()
	{
		string term = saveSettingsText;
		LocalizationMediator.GetTranslation(ref term);
		PopupLauncher.Instance.RequestPopup(PopupLauncher.PopupType.SmallChoice, new PopupContext(term, (Action)delegate
		{
			settings.SaveSettings();
			settings.SettingsSaved();
			CloseMenu();
		}, (Action)delegate
		{
			settings.LoadSettings();
			settings.Refresh();
			settings.SettingsRolledBack();
			CloseMenu();
		}));
	}

	private void LaunchRestoreDefaultSettingsPopup()
	{
		string term = restoreDefaultSettingsText;
		LocalizationMediator.GetTranslation(ref term);
		PopupLauncher.Instance.RequestPopup(PopupLauncher.PopupType.SmallChoice, new PopupContext(term, (Action)delegate
		{
			settings.LoadSettings();
			_settingsMenuControls.LoadDefaultMapping();
			settings.Refresh();
		}, null));
	}

	private void LaunchRestoreCurrentDefaultSettingsPopup()
	{
		string term = restoreCurrentWindowSettingsText;
		LocalizationMediator.GetTranslation(ref term);
		PopupLauncher.Instance.RequestPopup(PopupLauncher.PopupType.SmallChoice, new PopupContext(term, (Action)delegate
		{
			if (_currentMenu == _settingsMenuControls)
			{
				_settingsMenuControls.LoadDefaultMapping();
			}
			else
			{
				((SettingsTabContentController)_currentMenu).ResetTabSettings();
				settings.Refresh();
			}
		}, null));
	}

	public bool TryApplyDirtySettings()
	{
		bool result = false;
		TabContentController[] array = tabContents;
		for (int i = 0; i < array.Length; i++)
		{
			SettingsTabContentController settingsTabContentController = array[i] as SettingsTabContentController;
			if (settingsTabContentController.IsDirty)
			{
				result = true;
				settingsTabContentController.ApplySettingsIfDirty();
			}
		}
		return result;
	}

	public override void Activate()
	{
		base.Activate();
		CacheCurrentSettings();
		settings.Refresh();
	}

	private void UpdateScaleAdjusters()
	{
		if (rectScaleAdjusters != null)
		{
			UIRectScaleAdjuster[] array = rectScaleAdjusters;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].AdjustScale();
			}
		}
	}

	private void CacheCurrentSettings()
	{
		cachedSettings = new SettingsSnapshot
		{
			autoAim = settings.AutoAim,
			ultiSkip = settings.UltiSkip,
			damageNumbers = settings.DamageNumbers,
			healthBar = settings.HealthBar,
			languageIdx = settings.LanguageIdx,
			textSpeed = settings.TextSpeed,
			textWaitSpeed = settings.TextWaitSpeed,
			masterVolume = settings.MasterVolume,
			musicVolume = settings.MusicVolume,
			sfxVolume = settings.SFXVolume,
			voiceVolume = settings.VoiceVolume,
			ambienceVolume = settings.AmbienceVolume,
			vaOnOff = settings.VAOnOff,
			vaLangIdx = settings.VALangIdx,
			resolution = settings.Resolution,
			screenMode = settings.ScreenMode,
			quality = settings.Quality,
			vSync = settings.VSync,
			brightness = settings.Brightness,
			gamma = settings.Gamma,
			contrast = settings.Contrast,
			fpsLimit = settings.FpsLimit,
			bloomIntensity = settings.BloomIntensity,
			colorBlindMode = (int)settings.ColorBlindMode,
			colorBlindStrength = settings.ColorBlindStrength,
			cameraShake = settings.CameraShake,
			cursorScale = settings.CursorScale,
			cursorHue = settings.CursorHue,
			cursorSaturation = settings.CursorSaturation
		};
	}

	private bool HaveSettingsChanged()
	{
		if (cachedSettings.autoAim != settings.AutoAim)
		{
			return true;
		}
		if (cachedSettings.ultiSkip != settings.UltiSkip)
		{
			return true;
		}
		if (cachedSettings.damageNumbers != settings.DamageNumbers)
		{
			return true;
		}
		if (cachedSettings.healthBar != settings.HealthBar)
		{
			return true;
		}
		if (cachedSettings.languageIdx != settings.LanguageIdx)
		{
			return true;
		}
		if (cachedSettings.textSpeed != settings.TextSpeed)
		{
			return true;
		}
		if (cachedSettings.textWaitSpeed != settings.TextWaitSpeed)
		{
			return true;
		}
		if (!Mathf.Approximately(cachedSettings.masterVolume, settings.MasterVolume))
		{
			return true;
		}
		if (!Mathf.Approximately(cachedSettings.musicVolume, settings.MusicVolume))
		{
			return true;
		}
		if (!Mathf.Approximately(cachedSettings.sfxVolume, settings.SFXVolume))
		{
			return true;
		}
		if (!Mathf.Approximately(cachedSettings.voiceVolume, settings.VoiceVolume))
		{
			return true;
		}
		if (!Mathf.Approximately(cachedSettings.ambienceVolume, settings.AmbienceVolume))
		{
			return true;
		}
		if (cachedSettings.vaOnOff != settings.VAOnOff)
		{
			return true;
		}
		if (cachedSettings.vaLangIdx != settings.VALangIdx)
		{
			return true;
		}
		if (cachedSettings.resolution != settings.Resolution)
		{
			return true;
		}
		if (cachedSettings.screenMode != settings.ScreenMode)
		{
			return true;
		}
		if (cachedSettings.quality != settings.Quality)
		{
			return true;
		}
		if (cachedSettings.vSync != settings.VSync)
		{
			return true;
		}
		if (!Mathf.Approximately(cachedSettings.brightness, settings.Brightness))
		{
			return true;
		}
		if (!Mathf.Approximately(cachedSettings.gamma, settings.Gamma))
		{
			return true;
		}
		if (!Mathf.Approximately(cachedSettings.contrast, settings.Contrast))
		{
			return true;
		}
		if (cachedSettings.fpsLimit != settings.FpsLimit)
		{
			return true;
		}
		if (!Mathf.Approximately(cachedSettings.bloomIntensity, settings.BloomIntensity))
		{
			return true;
		}
		if (cachedSettings.colorBlindMode != (int)settings.ColorBlindMode)
		{
			return true;
		}
		if (!Mathf.Approximately(cachedSettings.colorBlindStrength, settings.ColorBlindStrength))
		{
			return true;
		}
		if (cachedSettings.cameraShake != settings.CameraShake)
		{
			return true;
		}
		if (!Mathf.Approximately(cachedSettings.cursorScale, settings.CursorScale))
		{
			return true;
		}
		if (!Mathf.Approximately(cachedSettings.cursorHue, settings.CursorHue))
		{
			return true;
		}
		if (!Mathf.Approximately(cachedSettings.cursorSaturation, settings.CursorSaturation))
		{
			return true;
		}
		return false;
	}
}
