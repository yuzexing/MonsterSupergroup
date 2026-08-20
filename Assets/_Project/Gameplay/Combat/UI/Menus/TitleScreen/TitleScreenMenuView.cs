using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.Cinematics;
using AstralShift.Control;
using AstralShift.FSM;
using AstralShift.HellMaiden.Audio;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Data;
using AstralShift.Helpers;
using AstralShift.UI;
using Cysharp.Threading.Tasks;
using FMODUnity;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Menus.TitleScreen
{
	public class TitleScreenMenuView : MonoBehaviour
	{
		[SerializeField]
		public TitleScreenController controller;

		[Space]
		[SerializeField]
		protected CanvasGroup splashCanvasGroup;

		[SerializeField]
		protected Button splashButton;

		[SerializeField]
		protected Animator pressAnyButton;

		[SerializeField]
		protected CanvasGroup menuCanvasGroup;

		[SerializeField]
		protected ShiftableOptions options;

		[SerializeField]
		protected Button continueButton;

		[SerializeField]
		protected Button newGameButton;

		[SerializeField]
		protected Button settingsButton;

		[SerializeField]
		protected Button creditsButton;

		[SerializeField]
		protected Button quitButton;

		private readonly List<Button> _mainButtons = new List<Button>();

		private readonly List<CustomUIButton> _socialsButtons = new List<CustomUIButton>();

		[Space]
		[SerializeField]
		protected PlayableDirector menuViewDirector;

		[SerializeField]
		protected StudioEventEmitter animationFmod;

		[SerializeField]
		private string parameterSkip = "SkipIntro";

		[SerializeField]
		protected CinematicPlayer titleVideo;

		[SerializeField]
		protected TimelineAsset transitionToSplash;

		[SerializeField]
		protected TimelineAsset splashLoop;

		[SerializeField]
		protected TimelineAsset transitionSplashToMainMenu;

		[SerializeField]
		protected TimelineAsset mainMenuLoop;

		[SerializeField]
		protected TimelineAsset transitionMainMenuToSettings;

		[SerializeField]
		protected TimelineAsset transitionSettingsToMainMenu;

		[SerializeField]
		protected float transitionToSplashSkipTimeStamp = 5.3f;

		[SerializeField]
		protected CustomUIButton discordButton;

		[SerializeField]
		protected string discordURL = "https://discord.com/invite/Hn5dkbAZR5";

		[SerializeField]
		protected CustomUIButton twitterButton;

		[SerializeField]
		protected string twitterURL = "https://x.com/AstralShiftPro";

		[SerializeField]
		protected CustomUIButton steamButton;

		[SerializeField]
		protected string steamURL = "https://store.steampowered.com/app/3372060/Hell_Maiden/";

		private bool _canSkipTitleSplashEnterAnimation;

		private readonly int _pressAnyButtonIsActiveHash = Animator.StringToHash("IsActive");

		private GameObject _currentSelection;

		private StateMachine _stateMachine;

		private State _begin;

		private State _titleVideoState;

		private State _toTitleSplashState;

		private State _titleSplashLoopState;

		private State _titleSplashLoopToMainMenuState;

		private State _mainMenuLoopState;

		private State _mainMenuToSettingsState;

		private State _settingsToMainMenuState;

		private State _mainMenuToCreditsState;

		private State _creditStateToMainMenu;

		private const int AllowTitleScreenEnterAnimationSkipDelayInMS = 1000;

		private const int AllowSplashInteractionDelayInMS = 500;

		private const int AllowMainMenuInteractionDelayInMS = 500;

		public TitleScreenController titleScreenController => controller;

		public bool IsMainMenuInteractable => menuCanvasGroup.interactable;

		public bool IsTitleSplashInteractable => splashCanvasGroup.interactable;

		public GameObject CurrentSelection => _currentSelection;

		public void Init()
		{
			EnableMenuInteraction(state: false);
			EnableTitleSplashInteraction(state: false);
			continueButton.onClick.AddListener(delegate
			{
				SetCurrentSelection(continueButton.gameObject);
			});
			newGameButton.onClick.AddListener(delegate
			{
				SetCurrentSelection(newGameButton.gameObject);
			});
			settingsButton.onClick.AddListener(delegate
			{
				SetCurrentSelection(settingsButton.gameObject);
			});
			creditsButton.onClick.AddListener(delegate
			{
				SetCurrentSelection(creditsButton.gameObject);
			});
			quitButton.onClick.AddListener(delegate
			{
				SetCurrentSelection(quitButton.gameObject);
			});
			_mainButtons.Add(continueButton);
			_mainButtons.Add(newGameButton);
			_mainButtons.Add(settingsButton);
			_mainButtons.Add(creditsButton);
			_mainButtons.Add(quitButton);
			discordButton.onSubmit.AddListener(OpenDiscord);
			twitterButton.onSubmit.AddListener(OpenTwitter);
			steamButton.onSubmit.AddListener(OpenSteam);
			discordButton.onSelect.AddListener(delegate
			{
				SetCurrentSelection(discordButton.gameObject);
			});
			twitterButton.onSelect.AddListener(delegate
			{
				SetCurrentSelection(twitterButton.gameObject);
			});
			steamButton.onSelect.AddListener(delegate
			{
				SetCurrentSelection(steamButton.gameObject);
			});
			_socialsButtons.Add(discordButton);
			_socialsButtons.Add(twitterButton);
			_socialsButtons.Add(steamButton);
			titleVideo.PreWarm();
			titleVideo.RawImage.color = Color.black;
			InitStateMachine();
		}

		private void InitStateMachine()
		{
			_stateMachine = new StateMachine("Title Screen Menu");
			_begin = new State("Begin");
			_titleVideoState = new State("Title Splash Video");
			_toTitleSplashState = new State("Transition To Title Splash");
			_titleSplashLoopState = new State("Title Splash Loop");
			_titleSplashLoopToMainMenuState = new State("Title Splash To Main Menu");
			_mainMenuLoopState = new State("Main Menu Loop");
			_mainMenuToSettingsState = new State("Transition Main Menu To Settings Menu");
			_settingsToMainMenuState = new State("Transition Settings Menu To Main Menu");
			_mainMenuToCreditsState = new State("Transition Main Menu To Credits");
			_creditStateToMainMenu = new State("Transition Credits To Main Menu");
			State titleVideoState = _titleVideoState;
			titleVideoState.onEnter = (Action)Delegate.Combine(titleVideoState.onEnter, new Action(OnEnterTitleVideo));
			State titleVideoState2 = _titleVideoState;
			titleVideoState2.onExit = (Action)Delegate.Combine(titleVideoState2.onExit, new Action(OnExitTitleVideo));
			State toTitleSplashState = _toTitleSplashState;
			toTitleSplashState.onEnter = (Action)Delegate.Combine(toTitleSplashState.onEnter, new Action(OnEnterTitleSplashScreen));
			State titleSplashLoopState = _titleSplashLoopState;
			titleSplashLoopState.onEnter = (Action)Delegate.Combine(titleSplashLoopState.onEnter, new Action(OnEnterTitleSplashLoop));
			State titleSplashLoopState2 = _titleSplashLoopState;
			titleSplashLoopState2.onExit = (Action)Delegate.Combine(titleSplashLoopState2.onExit, new Action(OnExitTitleSplashLoop));
			State titleSplashLoopToMainMenuState = _titleSplashLoopToMainMenuState;
			titleSplashLoopToMainMenuState.onEnter = (Action)Delegate.Combine(titleSplashLoopToMainMenuState.onEnter, new Action(EnterTitleSplashToMainMenu));
			State mainMenuLoopState = _mainMenuLoopState;
			mainMenuLoopState.onEnter = (Action)Delegate.Combine(mainMenuLoopState.onEnter, new Action(OnEnterMainMenuLoop));
			State mainMenuLoopState2 = _mainMenuLoopState;
			mainMenuLoopState2.onExit = (Action)Delegate.Combine(mainMenuLoopState2.onExit, new Action(OnExitMainMenuLoop));
			State mainMenuToSettingsState = _mainMenuToSettingsState;
			mainMenuToSettingsState.onEnter = (Action)Delegate.Combine(mainMenuToSettingsState.onEnter, new Action(OnEnterMainMenuToSettings));
			State settingsToMainMenuState = _settingsToMainMenuState;
			settingsToMainMenuState.onEnter = (Action)Delegate.Combine(settingsToMainMenuState.onEnter, new Action(OnEnterSettingsToMainMenu));
			State mainMenuToCreditsState = _mainMenuToCreditsState;
			mainMenuToCreditsState.onEnter = (Action)Delegate.Combine(mainMenuToCreditsState.onEnter, new Action(OnEnterMainMenuToCredits));
			State creditStateToMainMenu = _creditStateToMainMenu;
			creditStateToMainMenu.onEnter = (Action)Delegate.Combine(creditStateToMainMenu.onEnter, new Action(OnEnterCreditsToMainMenu));
			_stateMachine.AddTransition(_begin, _titleVideoState);
			_stateMachine.AddTransition(_titleVideoState, _toTitleSplashState);
			_stateMachine.AddTransition(_toTitleSplashState, _titleSplashLoopState);
			_stateMachine.AddTransition(_titleSplashLoopState, _titleSplashLoopToMainMenuState);
			_stateMachine.AddTransition(_titleSplashLoopToMainMenuState, _mainMenuLoopState);
			_stateMachine.AddTransition(_mainMenuLoopState, _mainMenuToSettingsState);
			_stateMachine.AddTransition(_mainMenuToSettingsState, _settingsToMainMenuState);
			_stateMachine.AddTransition(_settingsToMainMenuState, _mainMenuLoopState);
			_stateMachine.SetInitialStateNoCallbacks(_begin);
		}

		public void TransitionToTitleVideo()
		{
			_stateMachine.MakeTransition(_titleVideoState);
		}

		private void TransitionToTitleSplashScreen()
		{
			_stateMachine.MakeTransition(_toTitleSplashState);
		}

		private void TransitionToTitleSplashLoop()
		{
			_stateMachine.MakeTransition(_titleSplashLoopState);
		}

		private void TransitionTitleSplashToMainMenu()
		{
			_stateMachine.MakeTransition(_titleSplashLoopToMainMenuState);
		}

		private void TransitionToMainMenuLoop()
		{
			_stateMachine.MakeTransition(_mainMenuLoopState);
		}

		public void TransitionMainMenuToSettings()
		{
			_stateMachine.MakeTransition(_mainMenuToSettingsState);
		}

		public void TransitionSettingsToMainMenu()
		{
			_stateMachine.MakeTransition(_settingsToMainMenuState);
		}

		public void TransitionMainMenuToCredits()
		{
			_stateMachine.MakeTransition(_mainMenuToCreditsState);
		}

		public void TransitionCreditsToMainMenu()
		{
			_stateMachine.MakeTransition(_creditStateToMainMenu);
		}

		private async void OnEnterTitleVideo()
		{
			try
			{
				titleVideo.RawImage.color = Color.white;
				titleVideo.SetOnVideoEndCallback(TransitionToTitleSplashScreen);
				titleVideo.StartVideo();
				await UniTask.Delay(1000, DelayType.UnscaledDeltaTime);
				_canSkipTitleSplashEnterAnimation = true;
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private void OnExitTitleVideo()
		{
			titleVideo.RawImage.color = Color.clear;
		}

		private async void OnEnterTitleSplashScreen()
		{
			try
			{
				MusicPlayer.Instance.QueueMusic(controller.MusicReference.Guid);
				MusicPlayer.Instance.PlayNextMusic();
				menuViewDirector.Play(transitionToSplash, DirectorWrapMode.Hold);
				menuViewDirector.time = 0.0;
				menuViewDirector.OnEndCallback(TransitionToTitleSplashLoop);
				await UniTask.NextFrame(PlayerLoopTiming.PostLateUpdate);
				LayoutRebuilder.ForceRebuildLayoutImmediate(pressAnyButton.transform as RectTransform);
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private async void OnEnterTitleSplashLoop()
		{
			try
			{
				_canSkipTitleSplashEnterAnimation = false;
				menuViewDirector.Play(splashLoop, DirectorWrapMode.Loop);
				menuViewDirector.time = 0.0;
				await UniTask.Delay(500, DelayType.UnscaledDeltaTime);
				EnableTitleSplashInteraction(state: true);
				RegisterTitleSplashScreenBindings();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private void OnExitTitleSplashLoop()
		{
			EnableTitleSplashInteraction(state: false);
			UnRegisterTitleSplashScreenBindings();
		}

		private async void EnterTitleSplashToMainMenu()
		{
			_ = 1;
			try
			{
				bool flag = SaveManager.HasSaveFiles();
				continueButton.gameObject.SetActive(flag);
				UpdateButtonHierarchyOrder(flag);
				options.ReCalculate();
				options.LockOptions();
				menuViewDirector.Play(transitionSplashToMainMenu, DirectorWrapMode.Hold);
				menuViewDirector.time = 0.0;
				menuViewDirector.OnEndCallback(TransitionToMainMenuLoop);
				await UniTask.NextFrame(PlayerLoopTiming.PostLateUpdate);
				await UniTask.NextFrame(PlayerLoopTiming.PostLateUpdate);
				LayoutRebuilder.ForceRebuildLayoutImmediate(menuCanvasGroup.transform as RectTransform);
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private async void OnEnterMainMenuLoop()
		{
			try
			{
				menuViewDirector.Play(mainMenuLoop, DirectorWrapMode.Loop);
				menuViewDirector.time = 0.0;
				await UniTask.Delay(500, DelayType.UnscaledDeltaTime);
				EnableMenuInteraction(state: true);
				RegisterMainMenuBindings();
				titleScreenController.OnControllerTypeChanged();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private void OnExitMainMenuLoop()
		{
			EnableMenuInteraction(state: false);
			UnRegisterMainMenuBindings();
		}

		private void OnEnterMainMenuToSettings()
		{
			menuViewDirector.Play(transitionMainMenuToSettings, DirectorWrapMode.Hold);
			menuViewDirector.time = 0.0;
		}

		private void OnEnterSettingsToMainMenu()
		{
			menuViewDirector.Play(transitionSettingsToMainMenu, DirectorWrapMode.Hold);
			menuViewDirector.time = 0.0;
			menuViewDirector.OnEndCallback(delegate
			{
				TransitionToMainMenuLoop();
				SelectSettingsButton();
			});
		}

		private void SelectSettingsButton()
		{
			int num = _mainButtons.IndexOf(settingsButton);
			if (num >= 0)
			{
				SelectMainButtonOfIndex(num);
			}
		}

		private void OnEnterMainMenuToCredits()
		{
			menuViewDirector.Play(transitionMainMenuToSettings, DirectorWrapMode.Hold);
			menuViewDirector.time = 0.0;
		}

		private void OnEnterCreditsToMainMenu()
		{
			menuViewDirector.Play(transitionMainMenuToSettings, DirectorWrapMode.Hold);
			menuViewDirector.time = 0.0;
		}

		public void EnableMenuInteraction(bool state)
		{
			menuCanvasGroup.interactable = state;
			menuCanvasGroup.blocksRaycasts = state;
		}

		private void EnableTitleSplashInteraction(bool state)
		{
			splashCanvasGroup.interactable = state;
			splashCanvasGroup.blocksRaycasts = state;
			pressAnyButton.SetBool(_pressAnyButtonIsActiveHash, state);
		}

		private void SetCurrentSelection(GameObject selection)
		{
			_currentSelection = selection;
		}

		private void UpdateButtonHierarchyOrder(bool continueHasPriority)
		{
			if (continueHasPriority)
			{
				for (int i = 0; i < _mainButtons.Count; i++)
				{
					_mainButtons[i].transform.SetSiblingIndex(i);
				}
			}
		}

		public void TrySkipToSplashAnimation()
		{
			if (_stateMachine.GetState() == _titleVideoState && _canSkipTitleSplashEnterAnimation)
			{
				_canSkipTitleSplashEnterAnimation = false;
				if (!(menuViewDirector.time > (double)transitionToSplashSkipTimeStamp) && titleVideo.VideoPlayer.isPlaying)
				{
					titleVideo.SkipVideo();
				}
			}
		}

		public void ExecuteSplashButtonOnClick()
		{
			splashButton.onClick.Invoke();
		}

		public void SelectFocusedMainButton()
		{
			EventSystem.current.SetSelectedGameObject(null);
			SetMainButtonsInteractable(state: false);
			GameObject element = options.GetCurrentElement();
			_mainButtons.ForEach(delegate(Button button)
			{
				if (button.gameObject == element)
				{
					button.interactable = true;
					_currentSelection = element;
				}
			});
			EventSystem.current.SetSelectedGameObject(element);
			SetCurrentSelection(element);
		}

		public void SelectMainButtonOfIndex(int index)
		{
			EventSystem.current.SetSelectedGameObject(null);
			SetMainButtonsInteractable(state: false);
			GameObject element = options.GetElement(index);
			_mainButtons.ForEach(delegate(Button button)
			{
				if (button.gameObject == element)
				{
					button.interactable = true;
					_currentSelection = element;
				}
			});
			if (ControllerLifetime.ActiveControllerType != ControllerType.Mouse)
			{
				EventSystem.current.SetSelectedGameObject(element);
				SetCurrentSelection(element);
			}
			else
			{
				EventSystem.current.SetSelectedGameObject(null);
			}
		}

		public void CycleMainButtonsLeft()
		{
			if (_mainButtons.Any((Button button) => button.gameObject == _currentSelection))
			{
				options.ShiftLeft();
			}
		}

		public void CycleMainButtonsRight()
		{
			if (_mainButtons.Any((Button button) => button.gameObject == _currentSelection))
			{
				options.ShiftRight();
			}
		}

		private void SelectMainButtons()
		{
			if (IsSelectingSocialsButton())
			{
				SelectFocusedMainButton();
			}
		}

		private void SelectSocialsButtons()
		{
			if (IsSelectingMainButton())
			{
				EventSystem.current.SetSelectedGameObject(twitterButton.gameObject);
			}
		}

		private bool IsSelectingMainButton()
		{
			return _mainButtons.Any((Button button) => button.gameObject == _currentSelection);
		}

		private bool IsSelectingSocialsButton()
		{
			return _socialsButtons.Any((CustomUIButton button) => button.gameObject == _currentSelection);
		}

		private void RegisterMainMenuBindings()
		{
			controller.OnUIDirectionalLeftPressed += CycleMainButtonsLeft;
			controller.OnUIDirectionalRightPressed += CycleMainButtonsRight;
			controller.OnUIDirectionalUpPressed += SelectMainButtons;
			controller.OnUIDirectionalDownPressed += SelectSocialsButtons;
			ShiftableOptions shiftableOptions = options;
			shiftableOptions.OnOptionChanged = (Action<int>)Delegate.Combine(shiftableOptions.OnOptionChanged, new Action<int>(SelectMainButtonOfIndex));
			continueButton.onClick.AddListener(titleScreenController.ContinueGame);
			newGameButton.onClick.AddListener(titleScreenController.NewGame);
			settingsButton.onClick.AddListener(titleScreenController.OpenSettings);
			creditsButton.onClick.AddListener(titleScreenController.OpenCredits);
			quitButton.onClick.AddListener(titleScreenController.QuitGame);
		}

		private void UnRegisterMainMenuBindings()
		{
			controller.OnUIDirectionalLeftPressed -= CycleMainButtonsLeft;
			controller.OnUIDirectionalRightPressed -= CycleMainButtonsRight;
			controller.OnUIDirectionalUpPressed -= SelectMainButtons;
			controller.OnUIDirectionalDownPressed -= SelectSocialsButtons;
			ShiftableOptions shiftableOptions = options;
			shiftableOptions.OnOptionChanged = (Action<int>)Delegate.Remove(shiftableOptions.OnOptionChanged, new Action<int>(SelectMainButtonOfIndex));
			continueButton.onClick.RemoveListener(titleScreenController.ContinueGame);
			newGameButton.onClick.RemoveListener(titleScreenController.NewGame);
			settingsButton.onClick.RemoveListener(titleScreenController.OpenSettings);
			creditsButton.onClick.RemoveListener(titleScreenController.OpenCredits);
			quitButton.onClick.RemoveListener(titleScreenController.QuitGame);
			SetMainButtonsInteractable(state: false);
		}

		private void SetMainButtonsInteractable(bool state)
		{
			foreach (Button mainButton in _mainButtons)
			{
				mainButton.interactable = state;
			}
		}

		private void RegisterTitleSplashScreenBindings()
		{
			splashButton.onClick.AddListener(TransitionTitleSplashToMainMenu);
		}

		private void UnRegisterTitleSplashScreenBindings()
		{
			splashButton.onClick.RemoveListener(TransitionTitleSplashToMainMenu);
		}

		public void OpenDiscord()
		{
			Application.OpenURL(discordURL);
		}

		public void OpenTwitter()
		{
			Application.OpenURL(twitterURL);
		}

		public void OpenSteam()
		{
			Application.OpenURL(steamURL);
		}
	}
}
