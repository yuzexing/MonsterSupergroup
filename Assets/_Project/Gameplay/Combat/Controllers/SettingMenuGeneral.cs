using System;
using AstralShift.UI;
using UnityEngine.UI;

public class SettingMenuGeneral : SettingsTabContentController
{
	public ShiftableOptions language;

	public BinaryOption autoAim;

	public BinaryOption ultiSkip;

	public BinaryOption damageNumbers;

	public BinaryOption healthBar;

	public Slider UIOpacity;

	public Slider attackOpacity;

	public ShiftableOptions textSpeed;

	public ShiftableOptions textWait;

	private int steps = 20;

	private void OnEnable()
	{
		SettingsManager settingsManager = base.settings;
		settingsManager.OnRefresh = (Action)Delegate.Combine(settingsManager.OnRefresh, new Action(Refresh));
	}

	private void OnDisable()
	{
		SettingsManager settingsManager = base.settings;
		settingsManager.OnRefresh = (Action)Delegate.Remove(settingsManager.OnRefresh, new Action(Refresh));
	}

	private void Start()
	{
		UIOpacity.maxValue = steps;
		attackOpacity.maxValue = steps;
		ShiftableOptions shiftableOptions = language;
		shiftableOptions.OnOptionChanged = (Action<int>)Delegate.Combine(shiftableOptions.OnOptionChanged, new Action<int>(OnLanguageChanged));
		autoAim.onValueChanged.AddListener(delegate
		{
			OnAutoAimChange();
		});
		ultiSkip.onValueChanged.AddListener(delegate
		{
			OnUltimateSkipChange();
		});
		damageNumbers.onValueChanged.AddListener(delegate
		{
			OnDamageNumbersChange();
		});
		healthBar.onValueChanged.AddListener(delegate
		{
			OnHealthBarChange();
		});
		UIOpacity.onValueChanged.AddListener(delegate
		{
			OnUIOpacityChange();
		});
		attackOpacity.onValueChanged.AddListener(delegate
		{
			OnAttackOpacityChange();
		});
		ShiftableOptions shiftableOptions2 = textSpeed;
		shiftableOptions2.OnOptionChanged = (Action<int>)Delegate.Combine(shiftableOptions2.OnOptionChanged, new Action<int>(OnTextSpeedChange));
		ShiftableOptions shiftableOptions3 = textWait;
		shiftableOptions3.OnOptionChanged = (Action<int>)Delegate.Combine(shiftableOptions3.OnOptionChanged, new Action<int>(OnTextWaitChange));
		foreach (object value in Enum.GetValues(typeof(SettingsManager.Language)))
		{
			language.AddOption(value.ToString());
		}
		foreach (object value2 in Enum.GetValues(typeof(SettingsManager.Level)))
		{
			textSpeed.AddOption(value2.ToString());
		}
		foreach (object value3 in Enum.GetValues(typeof(SettingsManager.Level)))
		{
			textWait.AddOption(value3.ToString());
		}
		Refresh();
	}

	private void OnLanguageChanged(int index)
	{
		base.settings.SetLanguage(index);
		LocalizeDescription();
		Refresh();
	}

	public void OnAutoAimChange()
	{
		base.settings.SetAutoAim(autoAim.GetState());
	}

	private void OnUltimateSkipChange()
	{
		base.settings.SetUltiSkip(ultiSkip.GetState());
	}

	private void OnDamageNumbersChange()
	{
		base.settings.SetDamageNumbers(damageNumbers.GetState());
	}

	private void OnHealthBarChange()
	{
		base.settings.SetHealthBar(healthBar.GetState());
	}

	public void OnUIOpacityChange()
	{
		base.settings.SetUIOpacity(UIOpacity.value);
	}

	public void OnAttackOpacityChange()
	{
		base.settings.SetAttackOpacity(attackOpacity.value);
	}

	public void OnTextSpeedChange(int index)
	{
		base.settings.SetTextSpeed(index);
	}

	public void OnTextWaitChange(int index)
	{
		base.settings.SetTextWaitSpeed(index);
	}

	public void Refresh()
	{
		if (language != null)
		{
			language.LockOptions(base.settings.LanguageIdx);
		}
		if (textSpeed != null)
		{
			textSpeed.LockOptions(base.settings.TextSpeed);
		}
		if (textWait != null)
		{
			textWait.LockOptions(base.settings.TextWaitSpeed);
		}
		UIOpacity.value = base.settings.UIopacity * (float)steps;
		attackOpacity.value = base.settings.AttackOpacity * (float)steps;
		autoAim.SetDefaultState(base.settings.AutoAim);
		ultiSkip.SetDefaultState(base.settings.UltiSkip);
		damageNumbers.SetDefaultState(base.settings.DamageNumbers);
		healthBar.SetDefaultState(base.settings.HealthBar);
		SelectFirstSelectable();
	}

	public override void ApplySettingsIfDirty()
	{
	}

	public override void ResetTabSettings()
	{
		SettingsData.Instance.ResetGeneral();
	}
}
