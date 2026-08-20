using System;
using AstralShift.Control.Controllers;
using AstralShift.FSM;
using AstralShift.Managers;
using AstralShift.UI;
using Rewired;
using UnityEngine;

public class TabMenuController : GameMenuController
{
	[SerializeField]
	protected MenuTabSelector tabSelector;

	[SerializeField]
	protected TabContentController[] tabContents;

	[SerializeField]
	protected bool canWrap = true;

	protected TabContentController _currentMenu;

	protected int _selectedTabIndex;

	public bool blockInputs;

	public int NextTabIndex => (_selectedTabIndex + 1) % tabContents.Length;

	public bool IsLastTab => _selectedTabIndex == tabContents.Length - 1;

	public int PreviousTabIndex
	{
		get
		{
			if (_selectedTabIndex <= 0)
			{
				return tabContents.Length - 1;
			}
			return _selectedTabIndex - 1;
		}
	}

	public bool IsFirstTab => _selectedTabIndex == 0;

	public override void Init()
	{
		base.Init();
		tabSelector?.Init(canWrap);
		tabSelector?.PreviousButton?.onSubmit.AddListener(SelectPreviousTab);
		tabSelector?.NextButton?.onSubmit.AddListener(SelectNextTab);
		for (int i = 0; i < tabContents.Length; i++)
		{
			tabContents[i].Init();
		}
		onOpen.AddListener(SelectFirstTab);
	}

	public virtual void SelectTab(int tabIdx, bool instant = false)
	{
		if (tabIdx != _selectedTabIndex)
		{
			_currentMenu.Close(instant);
			_selectedTabIndex = tabIdx;
			tabSelector?.SelectTab(tabIdx);
			_currentMenu = tabContents[tabIdx];
			_currentMenu.Open(instant);
		}
	}

	protected virtual void SelectFirstTab()
	{
		_currentMenu = tabContents[_selectedTabIndex];
		tabSelector?.SelectTab(_selectedTabIndex);
		_currentMenu.Open();
	}

	private void SelectPreviousTab()
	{
		if (canWrap || !IsFirstTab)
		{
			SelectTab(PreviousTabIndex);
		}
	}

	private void SelectNextTab()
	{
		if (canWrap || !IsLastTab)
		{
			SelectTab(NextTabIndex);
		}
	}

	protected override void InitStateBehaviour()
	{
		onOpen.AddListener(delegate
		{
			EnableGameObject(state: true);
		});
		State disabled = Disabled;
		disabled.onEnter = (Action)Delegate.Combine(disabled.onEnter, (Action)delegate
		{
			EnableGameObject(state: false);
		});
	}

	public override void UILeftTrigger(InputActionEventData data)
	{
		if (!blockInputs)
		{
			base.UILeftTrigger(data);
			if (data.eventType == InputActionEventType.ButtonJustPressed)
			{
				SelectPreviousTab();
			}
		}
	}

	public override void UIRightTrigger(InputActionEventData data)
	{
		if (!blockInputs)
		{
			base.UIRightTrigger(data);
			if (data.eventType == InputActionEventType.ButtonJustPressed)
			{
				SelectNextTab();
			}
		}
	}

	protected virtual void CloseMenu()
	{
		Close();
	}

	protected override void OnClosingFinished()
	{
		base.OnClosingFinished();
		ControllerManager.Instance.YieldGameController();
	}

	public override void Activate()
	{
		base.Activate();
		PauseManager.Instance.PauseGame();
	}

	public override void Deactivate()
	{
		base.Deactivate();
		PauseManager.Instance.ResumeGame();
	}
}
