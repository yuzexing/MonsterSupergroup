using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AstralShift.UI;
using AstralShift.UI.PopupWindows;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class SettingMenuGraphics : SettingsTabContentController
{
	public ShiftableOptions display;

	public InteractableOptions resolution;

	public ShiftableOptions windowed;

	public Slider brightness;

	public Slider gamma;

	public Slider contrast;

	public BinaryOption vsync;

	public ShiftableOptions framecap;

	public BinaryOption HDR;

	public Slider bloom;

	[SerializeField]
	protected CanvasGroup applyChangesButton;

	private int _screenIndex;

	private int _resolution;

	private int _screenMode;

	private int _quality;

	private Coroutine _dirtySettingsTimer;

	private const float DirtySettingsTimeout = 10f;

	private const float bloomDivisor = 10f;

	public UISelectable multipleOptionResolutionRef;

	private UnityAction<float> _onBrightnessChangeDelegate;

	private UnityAction<float> _onGammaChangeDelegate;

	private UnityAction<float> _onContrastChangeDelegate;

	private UnityAction<float> _onBloomChangeDelegate;

	private Resolution[] resolutionArray;

	private bool _isApplyingResolution;

	private int _pendingResolution = -1;

	private void OnEnable()
	{
		List<DisplayInfo> screens = base.settings.GetScreens();
		if (display != null)
		{
			foreach (DisplayInfo item in screens)
			{
				display.AddOption(item.name, selected: false, localize: false);
			}
			ShiftableOptions shiftableOptions = display;
			shiftableOptions.OnOptionChanged = (Action<int>)Delegate.Combine(shiftableOptions.OnOptionChanged, new Action<int>(OnDisplayChange));
		}
		SetupResolutionsDrawer();
		if (resolution != null)
		{
			InteractableOptions interactableOptions = resolution;
			interactableOptions.OnOptionChanged = (Action<int>)Delegate.Combine(interactableOptions.OnOptionChanged, new Action<int>(OnResolutionConfirm));
		}
		if (brightness != null)
		{
			_onBrightnessChangeDelegate = delegate
			{
				OnBrightnessChange();
			};
			brightness.onValueChanged.AddListener(_onBrightnessChangeDelegate);
		}
		if (gamma != null)
		{
			_onGammaChangeDelegate = delegate
			{
				OnGammaChange();
			};
			gamma.onValueChanged.AddListener(_onGammaChangeDelegate);
		}
		if (contrast != null)
		{
			_onContrastChangeDelegate = delegate
			{
				OnContrastChange();
			};
			contrast.onValueChanged.AddListener(_onContrastChangeDelegate);
		}
		if (windowed != null)
		{
			foreach (object value in Enum.GetValues(typeof(SettingsManager.ScreenModeType)))
			{
				windowed.AddOption(value.ToString());
			}
			ShiftableOptions shiftableOptions2 = windowed;
			shiftableOptions2.OnOptionChanged = (Action<int>)Delegate.Combine(shiftableOptions2.OnOptionChanged, new Action<int>(OnScreenModeChange));
		}
		if (vsync != null)
		{
			vsync.onValueChanged.AddListener(OnVsyncChange);
		}
		if (HDR != null)
		{
			HDR.onValueChanged.AddListener(OnHDRChange);
		}
		if (framecap != null)
		{
			int[] frameCaps = base.settings.GetFrameCaps();
			for (int num = 0; num < frameCaps.Length; num++)
			{
				int num2 = frameCaps[num];
				if (num2 == -1)
				{
					framecap.AddOption("Off", selected: false, localize: false);
				}
				else
				{
					framecap.AddOption(num2.ToString(), selected: false, localize: false);
				}
			}
			ShiftableOptions shiftableOptions3 = framecap;
			shiftableOptions3.OnOptionChanged = (Action<int>)Delegate.Combine(shiftableOptions3.OnOptionChanged, new Action<int>(OnFrameCapChange));
		}
		if (bloom != null)
		{
			_onBloomChangeDelegate = delegate
			{
				OnBloomChange();
			};
			bloom.onValueChanged.AddListener(_onBloomChangeDelegate);
			bloom.value = base.settings.BloomIntensity * 10f;
		}
		if (base.mainController != null)
		{
			base.mainController.OnUIButtonSubmitJustPressed += OnSubmitPressed;
		}
		Refresh();
		SettingsManager settingsManager = base.settings;
		settingsManager.OnSettingsRolledBack = (Action)Delegate.Combine(settingsManager.OnSettingsRolledBack, new Action(RollbackSettings));
		SettingsManager settingsManager2 = base.settings;
		settingsManager2.OnRefresh = (Action)Delegate.Combine(settingsManager2.OnRefresh, new Action(Refresh));
		ResetDirty();
	}

	private void OnDisable()
	{
		if (display != null)
		{
			ShiftableOptions shiftableOptions = display;
			shiftableOptions.OnOptionChanged = (Action<int>)Delegate.Remove(shiftableOptions.OnOptionChanged, new Action<int>(OnDisplayChange));
		}
		if (resolution != null)
		{
			InteractableOptions interactableOptions = resolution;
			interactableOptions.OnOptionChanged = (Action<int>)Delegate.Remove(interactableOptions.OnOptionChanged, new Action<int>(OnResolutionConfirm));
		}
		if (brightness != null && _onBrightnessChangeDelegate != null)
		{
			brightness.onValueChanged.RemoveListener(_onBrightnessChangeDelegate);
		}
		if (gamma != null && _onGammaChangeDelegate != null)
		{
			gamma.onValueChanged.RemoveListener(_onGammaChangeDelegate);
		}
		if (contrast != null && _onContrastChangeDelegate != null)
		{
			contrast.onValueChanged.RemoveListener(_onContrastChangeDelegate);
		}
		if (windowed != null)
		{
			ShiftableOptions shiftableOptions2 = windowed;
			shiftableOptions2.OnOptionChanged = (Action<int>)Delegate.Remove(shiftableOptions2.OnOptionChanged, new Action<int>(OnScreenModeChange));
		}
		if (vsync != null)
		{
			vsync.onValueChanged.RemoveListener(OnVsyncChange);
		}
		if (HDR != null)
		{
			HDR.onValueChanged.RemoveListener(OnHDRChange);
		}
		if (framecap != null)
		{
			ShiftableOptions shiftableOptions3 = framecap;
			shiftableOptions3.OnOptionChanged = (Action<int>)Delegate.Remove(shiftableOptions3.OnOptionChanged, new Action<int>(OnFrameCapChange));
		}
		if (bloom != null && _onBloomChangeDelegate != null)
		{
			bloom.onValueChanged.RemoveListener(_onBloomChangeDelegate);
		}
		if (base.settings != null)
		{
			SettingsManager settingsManager = base.settings;
			settingsManager.OnSettingsRolledBack = (Action)Delegate.Remove(settingsManager.OnSettingsRolledBack, new Action(RollbackSettings));
			SettingsManager settingsManager2 = base.settings;
			settingsManager2.OnRefresh = (Action)Delegate.Remove(settingsManager2.OnRefresh, new Action(Refresh));
		}
		if (base.mainController != null)
		{
			base.mainController.OnUIButtonSubmitJustPressed -= OnSubmitPressed;
		}
		StopRollbackSettingsCountdown();
	}

	private void SetupResolutionsDrawer()
	{
		resolutionArray = base.settings.GetResolutions();
		if (resolutionArray != null && this.resolution != null)
		{
			this.resolution.RemoveAllOptions();
			Resolution[] array = resolutionArray;
			for (int i = 0; i < array.Length; i++)
			{
				Resolution resolution = array[i];
				this.resolution.AddOption(resolution.width + "x" + resolution.height, selected: false, localize: false);
			}
		}
	}

	private void OnResolutionConfirm(int _)
	{
		LaunchResolutionSettingsScrollablePopup();
	}

	private void OnSubmitPressed()
	{
		resolution.OnConfirmButtonClicked();
	}

	public void OnBrightnessChange()
	{
		base.settings.SetBrightness(brightness.value);
	}

	public void OnGammaChange()
	{
		base.settings.SetGamma(gamma.value);
	}

	public void OnContrastChange()
	{
		base.settings.SetContrast(contrast.value);
	}

	private async void OnDisplayChange(int index)
	{
		try
		{
			await base.settings.SetDisplay(index);
			SetupResolutionsDrawer();
			_resolution = (base.settings.Resolution = (SettingsData.Instance.Resolution = 0));
			base.settings.ApplyGraphicsSettings(base.settings.Resolution, _screenMode, _quality);
			ResetDirty();
			Refresh();
		}
		catch (Exception ex)
		{
			Debug.LogError("Failed to change display: " + ex.Message);
			RollbackSettings();
		}
	}

	public void OnResolutionChange(int index)
	{
		if (!_isApplyingResolution)
		{
			_pendingResolution = index;
			if (base.settings.Resolution != index)
			{
				SetDirty("OnResolutionChange", state: true);
				Debug.Log($"Resolution changed to index: {index}");
			}
			else
			{
				SetDirty("OnResolutionChange", state: false);
				_pendingResolution = -1;
			}
		}
	}

	private void OnScreenModeChange(int index)
	{
		_screenMode = index;
		if (base.settings.ScreenMode != _screenMode)
		{
			SetDirty("OnScreenModeChange", state: true);
		}
		else
		{
			SetDirty("OnScreenModeChange", state: false);
		}
	}

	private void OnQualityChange(int index)
	{
		_quality = index;
	}

	private void OnVsyncChange(int index)
	{
		base.settings.SetVsync(index);
	}

	private void OnHDRChange(int index)
	{
		base.settings.SetHDR(index);
	}

	private void OnFrameCapChange(int index)
	{
		base.settings.SetFramerateLimit(index);
	}

	private void OnBloomChange()
	{
		base.settings.ChangeBloom(bloom.value / 10f);
	}

	public void TryLaunchDirtySettingsPopup()
	{
		if (base.IsDirty)
		{
			ApplySettingsIfDirty();
			Canvas.ForceUpdateCanvases();
			string term = "STT_KeepSettings";
			LocalizationMediator.GetTranslation(ref term);
			PopupLauncher.Instance.RequestPopup(PopupLauncher.PopupType.SmallChoice, new PopupContext(string.Format(term, 10f), (Action)delegate
			{
				StopRollbackSettingsCountdown();
				SelectFirstSelectable();
			}, (Action)delegate
			{
				StopRollbackSettingsCountdown();
				RollbackSettings();
				Refresh();
				SelectFirstSelectable();
			}), null, InitRollBackSettingsCountdown);
		}
	}

	public void LaunchResolutionSettingsScrollablePopup()
	{
		if (!(currentSelected != multipleOptionResolutionRef) && resolutionArray != null)
		{
			string[] array = resolutionArray.Select((Resolution r) => $"{r.width} x {r.height}").ToArray();
			int[] array2 = Enumerable.Range(0, resolutionArray.Length).ToArray();
			PopupLauncher.Instance.RequestPopup(PopupLauncher.PopupType.ScrollBarMultipleChoice, new PopupContext(array, array2, new Action<int>(OnResolutionChange), new Action(SelectFirstSelectable), new Action(TryLaunchDirtySettingsPopup)));
		}
	}

	private void InitRollBackSettingsCountdown(PopupWindow window)
	{
		StopRollbackSettingsCountdown();
		_dirtySettingsTimer = null;
		_dirtySettingsTimer = StartCoroutine(RollbackSettingsCountDown(window));
	}

	private void StopRollbackSettingsCountdown()
	{
		if (_dirtySettingsTimer != null)
		{
			StopCoroutine(_dirtySettingsTimer);
			_dirtySettingsTimer = null;
		}
	}

	private IEnumerator RollbackSettingsCountDown(PopupWindow window)
	{
		if (window == null || window.gameObject == null)
		{
			_dirtySettingsTimer = null;
			yield break;
		}
		string text = "STT_KeepSettings";
		LocalizationMediator.GetTranslation(ref text);
		PopupWindowText textComponent = window.Components.FirstOrDefault((PopupWindowComponent element) => element is PopupWindowText) as PopupWindowText;
		int timer = 10;
		while (timer > 0)
		{
			yield return new WaitForSecondsRealtime(1f);
			timer--;
			if (window == null || window.gameObject == null)
			{
				_dirtySettingsTimer = null;
				yield break;
			}
			textComponent?.SetContext(new PopupContext(string.Format(text, timer)));
		}
		if (window != null && window.gameObject != null && window.cancelButton != null)
		{
			window.cancelButton.onClick.Invoke();
		}
		_dirtySettingsTimer = null;
	}

	private void RollbackSettings()
	{
		try
		{
			_screenIndex = (base.settings.ScreenIndex = SettingsData.Instance.ScreenIndex);
			_resolution = SettingsData.Instance.Resolution;
			_screenMode = SettingsData.Instance.ScreenMode;
			_quality = SettingsData.Instance.Quality;
			base.settings.ApplyGraphicsSettings(_resolution, _screenMode, _quality);
			if (display != null)
			{
				display.LockOptions(_screenIndex);
			}
			if (resolution != null)
			{
				resolution.LockOptions(_resolution);
			}
			if (windowed != null)
			{
				windowed.LockOptions(_screenMode);
			}
			ResetDirty();
		}
		catch (Exception ex)
		{
			Debug.LogError("Failed to rollback settings: " + ex.Message);
		}
	}

	private void Refresh()
	{
		_screenIndex = base.settings.ScreenIndex;
		_resolution = base.settings.Resolution;
		_quality = base.settings.Quality;
		_screenMode = base.settings.ScreenMode;
		if (brightness != null)
		{
			brightness.value = base.settings.Brightness;
		}
		if (gamma != null)
		{
			gamma.value = base.settings.Gamma;
		}
		if (contrast != null)
		{
			contrast.value = base.settings.Contrast;
		}
		if (display != null)
		{
			display.LockOptions(base.settings.ScreenIndex);
		}
		if (resolution != null)
		{
			resolution.LockOptions(base.settings.Resolution);
		}
		if (windowed != null)
		{
			windowed.LockOptions(base.settings.ScreenMode);
		}
		if (vsync != null)
		{
			vsync.SetDefaultState(base.settings.VSync == 1);
		}
		if (framecap != null)
		{
			framecap.LockOptions(base.settings.FpsLimit);
		}
		SelectFirstSelectable();
	}

	protected override void SetDirty(string key, bool state)
	{
		base.SetDirty(key, state);
		if (applyChangesButton != null)
		{
			applyChangesButton.alpha = (base.IsDirty ? 1f : 0f);
		}
	}

	protected override void ResetDirty()
	{
		base.ResetDirty();
		if (applyChangesButton != null)
		{
			applyChangesButton.alpha = 0f;
		}
		_pendingResolution = -1;
		_isApplyingResolution = false;
	}

	public void ApplySettings()
	{
		try
		{
			if (_pendingResolution != -1)
			{
				_isApplyingResolution = true;
				base.settings.ApplyGraphicsSettings(_pendingResolution, _screenMode, _quality);
				_resolution = _pendingResolution;
				_pendingResolution = -1;
				_isApplyingResolution = false;
			}
			else
			{
				base.settings.ApplyGraphicsSettings(_resolution, _screenMode, _quality);
			}
			ResetDirty();
			Refresh();
		}
		catch (Exception ex)
		{
			Debug.LogError("Failed to apply settings: " + ex.Message);
			_isApplyingResolution = false;
		}
	}

	public override void ApplySettingsIfDirty()
	{
		if (base.IsDirty)
		{
			ApplySettings();
		}
	}

	public override void ResetTabSettings()
	{
		SettingsData.Instance.ResetGraphics();
	}
}
