using System;
using System.Collections.Generic;
using AstralShift.Control;
using AstralShift.Helpers.Attributes;
using AstralShift.UI;
using I2.Loc;
using Rewired;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public abstract class SettingsTabContentController : TabContentController
{
	public VerticalLayoutGroup verticalLayout;

	[SerializeField]
	[NotNullRef]
	public AutomaticScroll autoScroll;

	[SerializeField]
	private TextMeshProUGUI descriptionText;

	[SerializeField]
	private TMP_Text resetCurrentTabButtonText;

	[SerializeField]
	private string resetCurrentTabButtonlocalizeId;

	private readonly HashSet<string> _dirtyOptions = new HashSet<string>();

	public SettingsMenuController mainController { get; set; }

	public SettingsManager settings { get; set; }

	public bool IsDirty => _dirtyOptions.Count > 0;

	public bool IsDirtyFor(string key)
	{
		return _dirtyOptions.Contains(key);
	}

	public override void Init()
	{
		base.Init();
		GenerateButtonNavigation();
		LocalizationManager.OnLocalizeEvent += LocalizeCurrentTabResetButtonName;
		SettingsManager settingsManager = settings;
		settingsManager.OnRefresh = (Action)Delegate.Combine(settingsManager.OnRefresh, new Action(SelectFirstSelectable));
	}

	protected virtual void OnDestroy()
	{
		LocalizationManager.OnLocalizeEvent -= LocalizeCurrentTabResetButtonName;
		SettingsManager settingsManager = settings;
		settingsManager.OnRefresh = (Action)Delegate.Remove(settingsManager.OnRefresh, new Action(SelectFirstSelectable));
	}

	public override void Open(bool instant = false)
	{
		base.Open(instant);
		LocalizeDescription();
		LocalizeCurrentTabResetButtonName();
	}

	private void LocalizeCurrentTabResetButtonName()
	{
		if (resetCurrentTabButtonText != null)
		{
			string term = resetCurrentTabButtonlocalizeId;
			LocalizationMediator.GetTranslation(ref term);
			resetCurrentTabButtonText.text = term;
		}
		LayoutRebuilder.ForceRebuildLayoutImmediate(resetCurrentTabButtonText.transform.parent.transform as RectTransform);
	}

	protected void LocalizeDescription()
	{
		if (descriptionText != null)
		{
			string term = base.Description;
			LocalizationMediator.GetTranslation(ref term);
			descriptionText.text = term;
		}
	}

	protected override void OnOpeningFinished()
	{
		base.OnOpeningFinished();
		SelectFirstSelectable();
	}

	protected virtual void SelectFirstSelectable()
	{
		if (firstSelected == null)
		{
			EventSystem.current.SetSelectedGameObject(null);
		}
		else if (ControllerLifetime.ActiveControllerType == ControllerType.Mouse)
		{
			EventSystem.current.SetSelectedGameObject(null);
			currentSelected = firstSelected;
		}
		else
		{
			EventSystem.current.SetSelectedGameObject(firstSelected.gameObject);
			currentSelected = firstSelected;
		}
	}

	protected virtual void GenerateButtonNavigation()
	{
		List<UISelectable> list = new List<UISelectable>();
		List<GameObject> list2 = new List<GameObject>();
		for (int i = 0; i < verticalLayout.transform.childCount; i++)
		{
			if (verticalLayout.transform.GetChild(i).gameObject.TryGetComponent<UISelectable>(out var component))
			{
				list.Add(component);
			}
		}
		for (int j = 0; j < list.Count; j++)
		{
			UISelectable selectable = list[j];
			if (selectable == null)
			{
				continue;
			}
			Navigation navigation = new Navigation
			{
				mode = Navigation.Mode.Explicit
			};
			if (j == 0)
			{
				firstSelected = selectable;
			}
			if (j > 0)
			{
				navigation.selectOnUp = list[j - 1];
			}
			if (j < list.Count - 1)
			{
				navigation.selectOnDown = list[j + 1];
			}
			selectable.navigation = navigation;
			selectable.onSelect.AddListener(delegate
			{
				currentSelected = selectable;
				if (selectable is SettingsUISelectable settingsUISelectable && descriptionText != null)
				{
					string term = settingsUISelectable.Description;
					LocalizationMediator.GetTranslation(ref term);
					descriptionText.text = term;
				}
				if (autoScroll != null)
				{
					autoScroll.ScrollToSelectedObject(selectable.transform as RectTransform);
				}
			});
			selectable.onPointerEnter.AddListener(delegate
			{
				currentSelected = selectable;
				if (selectable is SettingsUISelectable settingsUISelectable && descriptionText != null)
				{
					string term = settingsUISelectable.Description;
					LocalizationMediator.GetTranslation(ref term);
					descriptionText.text = term;
				}
			});
			selectable.onPointerExit.AddListener(delegate
			{
				if (descriptionText != null)
				{
					string term = base.Description;
					LocalizationMediator.GetTranslation(ref term);
					descriptionText.text = term;
				}
			});
			selectable.onDeSelect.AddListener(delegate
			{
				if (descriptionText != null)
				{
					string term = base.Description;
					LocalizationMediator.GetTranslation(ref term);
					descriptionText.text = term;
				}
			});
		}
		foreach (GameObject item in list2)
		{
			item.transform.SetParent(null, worldPositionStays: true);
			UnityEngine.Object.Destroy(item);
		}
	}

	protected virtual void ResetDirty()
	{
		_dirtyOptions.Clear();
	}

	protected virtual void SetDirty(string key, bool state)
	{
		if (state)
		{
			_dirtyOptions.Add(key);
		}
		else
		{
			_dirtyOptions.Remove(key);
		}
	}

	public abstract void ApplySettingsIfDirty();

	public abstract void ResetTabSettings();
}
