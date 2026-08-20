using System;
using System.Collections.Generic;
using AstralShift.Control;
using AstralShift.Control.Controllers;
using AstralShift.FSM;
using AstralShift.HellMaiden;
using AstralShift.HellMaiden.Scenes;
using AstralShift.HellMaiden.UI;
using AstralShift.Managers;
using AstralShift.UI;
using AstralShift.UI.PopupWindows;
using DG.Tweening;
using FMODUnity;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PauseMenuController : GameMenuController
{
	[SerializeField]
	private CanvasGroup pauseMenuGroup;

	[SerializeField]
	private CustomUIButton continueButton;

	[SerializeField]
	private CustomUIButton settingsButton;

	[SerializeField]
	private CustomUIButton settingsControlsButton;

	[SerializeField]
	private CustomUIButton giveUpButton;

	[SerializeField]
	private CustomUIButton quitButton;

	[SerializeField]
	private List<CustomUIButton> pauseMenuButtons;

	[SerializeField]
	private MenuTabSelector tabSelector;

	[Header("Rotation")]
	[SerializeField]
	private RectTransform circleToRotate;

	[SerializeField]
	private float rotationTime = 0.2f;

	[SerializeField]
	private CustomAnimationCurve rotationCurve;

	[SerializeField]
	private EventReference pauseIn;

	[SerializeField]
	private EventReference pauseOut;

	private bool _resetToFirstTabOnClose;

	private const float RotateAmount = 13.846f;

	private Tween _rotateTween;

	public static bool blockOpenAction { get; private set; }

	public override void Init()
	{
		base.Init();
		RegisterButtonsActions();
		SetCanvasGroupState(state: false);
		tabSelector.Init();
		EnableGameObject(state: false);
	}

	private void RegisterButtonsActions()
	{
		continueButton.onSubmit.AddListener(Close);
		settingsButton.onSubmit.AddListener(SettingsSubmit);
		settingsControlsButton.onSubmit.AddListener(ControlsSubmit);
		giveUpButton.onSubmit.AddListener(GiveUpSubmit);
		quitButton.onSubmit.AddListener(QuitSubmit);
		ConstructNavigation();
	}

	public override void Activate()
	{
		base.Activate();
		if (SceneManager.GetActiveScene().name == SceneEnum.Hub.ToString())
		{
			giveUpButton.interactable = false;
		}
		else
		{
			giveUpButton.interactable = true;
		}
		blockOpenAction = true;
		Open();
		ControllerLifetime.OnControllerChanged += PointerManager.Instance.SetPointerForMenuNavigation;
		PointerManager.Instance.SetPointerForMenuNavigation();
		if (CombatUIManager.Instance != null)
		{
			CombatUIManager.Instance.OpenHUD();
			CombatUIManager.Instance.SelectiveHUD(new CombatUIManager.SelectiveHudRequest
			{
				keepBars = true,
				keepClock = true,
				keepUltimate = true
			});
		}
		PauseManager.Instance.PauseGame();
	}

	public override void Deactivate()
	{
		base.Deactivate();
		if (CombatUIManager.Instance != null)
		{
			CombatUIManager.Instance.CloseHUD();
		}
		ControllerLifetime.OnControllerChanged -= PointerManager.Instance.SetPointerForMenuNavigation;
		PauseManager.Instance.ResumeGame();
	}

	private void ConstructNavigation()
	{
		foreach (CustomUIButton button in pauseMenuButtons)
		{
			Navigation navigation = new Navigation
			{
				mode = Navigation.Mode.Explicit
			};
			for (int i = 0; i < pauseMenuButtons.Count; i++)
			{
				if (i == 0)
				{
					firstSelectable = pauseMenuButtons[i];
				}
				CustomUIButton selectOnUp;
				if (i <= 0)
				{
					List<CustomUIButton> list = pauseMenuButtons;
					selectOnUp = list[list.Count - 1];
				}
				else
				{
					selectOnUp = pauseMenuButtons[i - 1];
				}
				navigation.selectOnUp = selectOnUp;
				navigation.selectOnDown = ((i < pauseMenuButtons.Count - 1) ? pauseMenuButtons[i + 1] : pauseMenuButtons[0]);
				pauseMenuButtons[i].navigation = navigation;
			}
			button.onSelect.AddListener(delegate
			{
				currentSelectable = button;
				RotateCircle(button.transform.GetSiblingIndex());
				tabSelector.SelectTab(currentSelectable.transform.GetSiblingIndex());
			});
			button.onPointerEnter.AddListener(delegate
			{
				currentSelectable = button;
				RotateCircle(button.transform.GetSiblingIndex());
				tabSelector.SelectTab(currentSelectable.transform.GetSiblingIndex());
			});
		}
	}

	private void SettingsSubmit()
	{
		if (base.IsActive)
		{
			CloseAnimation();
			RuntimeManager.PlayOneShot(pauseOut);
			SettingsMenuController settingsMenuController = ControllerManager.Instance.OverrideGameController<SettingsMenuController>();
			settingsMenuController.Open();
			if (_resetToFirstTabOnClose)
			{
				settingsMenuController.GoToGeneral();
				_resetToFirstTabOnClose = false;
			}
		}
	}

	private void ControlsSubmit()
	{
		if (base.IsActive)
		{
			CloseAnimation();
			RuntimeManager.PlayOneShot(pauseOut);
			SettingsMenuController settingsMenuController = ControllerManager.Instance.OverrideGameController<SettingsMenuController>();
			settingsMenuController.Open();
			settingsMenuController.GoToControls();
			_resetToFirstTabOnClose = true;
		}
	}

	private void GiveUpSubmit()
	{
		if (base.IsActive)
		{
			SetCanvasGroupState(state: false);
			string term = "GEN_GiveUpMsg";
			LocalizationMediator.GetTranslation(ref term);
			PopupLauncher.Instance.RequestPopup(PopupLauncher.PopupType.SmallChoice, new PopupContext(term, (Action)delegate
			{
				Close();
				GameDirector.Instance.Player.GiveUp();
			}, (Action)delegate
			{
				SetCanvasGroupState(state: true);
				OnOpeningFinished();
			}));
		}
	}

	private void QuitSubmit()
	{
		if (base.IsActive)
		{
			SetCanvasGroupState(state: false);
			string term = "GEN_QuitGameMsg";
			LocalizationMediator.GetTranslation(ref term);
			PopupLauncher.Instance.RequestPopup(PopupLauncher.PopupType.SmallChoice, new PopupContext(term, (Action)delegate
			{
				Close();
				SceneMaster.Instance.LoadScene(SceneEnum.TitleScreen);
			}, (Action)delegate
			{
				SetCanvasGroupState(state: true);
				OnOpeningFinished();
			}));
		}
	}

	private void SetCanvasGroupState(bool state)
	{
		pauseMenuGroup.interactable = state;
		pauseMenuGroup.blocksRaycasts = state;
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

	private void RotateCircle(int idToRotate)
	{
		_rotateTween.Kill();
		_rotateTween = circleToRotate.DORotate(new Vector3(0f, 0f, 13.846f * (float)idToRotate), rotationTime);
		_rotateTween.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
		_rotateTween.SetEase(rotationCurve.GetEaseFunction());
		_rotateTween.Play();
	}

	public override void UICenter2(InputActionEventData data)
	{
		if (data.eventType == InputActionEventType.ButtonJustPressed && !blockOpenAction)
		{
			continueButton?.OnSubmit(null);
		}
	}

	public override void UICancelPressed(InputActionEventData data)
	{
		continueButton?.OnSubmit(null);
	}

	public override void Open()
	{
		SetCanvasGroupState(state: false);
		RuntimeManager.PlayOneShot(pauseIn);
		base.Open();
	}

	protected override void OnOpeningFinished()
	{
		base.OnOpeningFinished();
		tabSelector.SelectIntroTab();
		SetCanvasGroupState(state: true);
		if (ControllerLifetime.ActiveControllerType == ControllerType.Mouse)
		{
			EventSystem.current.SetSelectedGameObject(null);
		}
		else if (currentSelectable != null)
		{
			EventSystem.current.SetSelectedGameObject((currentSelectable != null) ? currentSelectable.gameObject : firstSelectable.gameObject);
		}
		else if (firstSelectable != null)
		{
			EventSystem.current.SetSelectedGameObject(firstSelectable.gameObject);
		}
		else
		{
			EventSystem.current.SetSelectedGameObject(null);
		}
		blockOpenAction = false;
	}

	public override void Close()
	{
		blockOpenAction = true;
		RuntimeManager.PlayOneShot(pauseOut);
		currentSelectable = null;
		EventSystem.current.SetSelectedGameObject(null);
		base.Close();
		SetCanvasGroupState(state: false);
		ControllerManager.Instance.YieldGameController();
	}

	protected override void OnClosingFinished()
	{
		base.OnClosingFinished();
		blockOpenAction = false;
	}
}
