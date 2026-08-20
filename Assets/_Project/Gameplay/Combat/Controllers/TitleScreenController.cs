using System;
using AstralShift.Cinematics;
using AstralShift.Control;
using AstralShift.Control.Controllers;
using AstralShift.FSM;
using AstralShift.HellMaiden.Audio;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Scenes;
using AstralShift.HellMaiden.UI;
using AstralShift.HellMaiden.UI.Menus.TitleScreen;
using AstralShift.Managers;
using AstralShift.UI.PopupWindows;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using FMODUnity;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.HellMaiden.Controllers
{
	public class TitleScreenController : UIController
	{
		[Serializable]
		protected class ASSplashScreen
		{
			[SerializeField]
			protected CanvasGroup canvasGroup;

			[SerializeField]
			protected float fadeTime;

			[SerializeField]
			protected float duration;

			public CanvasGroup CanvasGroup => canvasGroup;

			public float FadeTime => fadeTime;

			public float Duration => duration;
		}

		[SerializeField]
		protected EventReference musicReference;

		[Space]
		[SerializeField]
		protected ASSplashScreen[] splashScreens;

		private int _currentBootSplashScreenIndex;

		private bool _isFadingBootSplash;

		private Sequence _bootSplashTween;

		[Space]
		[SerializeField]
		private CinematicPlayer openingVideoPlayer;

		[SerializeField]
		private CanvasGroup openingVideoCanvasGroup;

		[SerializeField]
		private float openingFadeTime = 0.5f;

		[SerializeField]
		private float allowOpeningSkipAfterSeconds = 5f;

		private Sequence _openingTween;

		private bool _canSkipOpeningVideo;

		[Space]
		[SerializeField]
		protected TitleScreenMenuView titleScreenMenuView;

		[SerializeField]
		private string newGameWarningKey;

		private StateMachine _stateMachine;

		private State _disabled;

		private State _begin;

		private State _bootSplashes;

		private State _opening;

		private State _titleMenu;

		private State _settingsMenu;

		private State _creditsMenu;

		private State _end;

		private State _quit;

		private State _continueGame;

		private State _newGame;

		private static bool FirstInitialization { get; set; } = true;

		public EventReference MusicReference => musicReference;

		public bool IsFadingBootSplash => _isFadingBootSplash;

		public event Action OnUIDirectionalLeftPressed;

		public event Action OnUIDirectionalRightPressed;

		public event Action OnUIDirectionalUpPressed;

		public event Action OnUIDirectionalDownPressed;

		public override void Init()
		{
			if (SceneMaster.Instance.FirstScene != SceneEnum.TitleScreen)
			{
				FirstInitialization = false;
			}
			titleScreenMenuView.Init();
			_stateMachine = new StateMachine("Title Screen Controller");
			_begin = new State("Begin");
			_bootSplashes = new State("Boot Splashes");
			_opening = new State("Opening");
			_titleMenu = new State("Title Menu");
			_creditsMenu = new State("Credits");
			_end = new State("End");
			_disabled = new State("Disabled");
			_quit = new State("Quitting");
			_settingsMenu = new State("Settings Menu");
			_continueGame = new State("Continue Game");
			_newGame = new State("New Game");
			_stateMachine.AddTransition(_begin, _bootSplashes);
			_stateMachine.AddTransition(_begin, _opening);
			_stateMachine.AddTransition(_begin, _titleMenu);
			_stateMachine.AddTransition(_bootSplashes, _opening);
			_stateMachine.AddTransition(_opening, _titleMenu);
			_stateMachine.AddTransition(_titleMenu, _end);
			_stateMachine.AddTransition(_titleMenu, _settingsMenu);
			_stateMachine.AddTransition(_settingsMenu, _titleMenu);
			_stateMachine.AddTransition(_titleMenu, _creditsMenu);
			_stateMachine.AddTransition(_creditsMenu, _titleMenu);
			_stateMachine.AddTransition(_titleMenu, _continueGame);
			_stateMachine.AddTransition(_titleMenu, _newGame);
			_stateMachine.AddAnyTransition(_quit);
			_stateMachine.AddAnyTransition(_disabled);
			_stateMachine.AddAnyTransition(_begin);
			InitDefaultStateBehaviour();
			_stateMachine.SetInitialState(_begin);
			MusicPlayer.Instance.StopAllMusic();
		}

		public override void Activate()
		{
			base.Activate();
			MusicPlayer.Instance.SetSnapShot(MusicPlayer.SnapshotID.Normal);
			PointerManager.Instance.HideMouseCursor();
			ControllerLifetime.OnControllerChanged += OnControllerTypeChanged;
			if (FirstInitialization)
			{
				openingVideoPlayer.PreWarm();
				TransitionToSplashes();
			}
			else
			{
				ControllerLifetime.OnControllerChanged += PointerManager.Instance.SetUIPointer;
				PointerManager.Instance.SetUIPointer();
				TransitionToTitleMenu();
			}
		}

		public override void Deactivate()
		{
			base.Deactivate();
			ControllerLifetime.OnControllerChanged -= PointerManager.Instance.SetUIPointer;
			ControllerLifetime.OnControllerChanged -= OnControllerTypeChanged;
		}

		public void OnDestroy()
		{
			_bootSplashTween?.Kill();
			_openingTween?.Kill();
			DOTween.Kill(this);
			ControllerLifetime.OnControllerChanged -= PointerManager.Instance.SetUIPointer;
			ControllerLifetime.OnControllerChanged -= OnControllerTypeChanged;
			ControllerManager.Instance.UnSubscribe(this);
			MusicPlayer.Instance.StopAllMusic();
		}

		private void InitDefaultStateBehaviour()
		{
			State disabled = _disabled;
			disabled.onEnter = (Action)Delegate.Combine(disabled.onEnter, (Action)delegate
			{
				ControllerManager.Instance.YieldGameController();
			});
			State begin = _begin;
			begin.onEnter = (Action)Delegate.Combine(begin.onEnter, (Action)delegate
			{
				ASSplashScreen[] array = splashScreens;
				foreach (ASSplashScreen obj in array)
				{
					obj.CanvasGroup.alpha = 0f;
					obj.CanvasGroup.interactable = false;
					obj.CanvasGroup.blocksRaycasts = false;
				}
				openingVideoCanvasGroup.alpha = 0f;
			});
			State bootSplashes = _bootSplashes;
			bootSplashes.onEnter = (Action)Delegate.Combine(bootSplashes.onEnter, (Action)delegate
			{
				_currentBootSplashScreenIndex = 0;
				_bootSplashTween?.Kill();
				_bootSplashTween = DOTween.Sequence(this);
				for (int i = 0; i < splashScreens.Length; i++)
				{
					ASSplashScreen aSSplashScreen = splashScreens[i];
					float duration = aSSplashScreen.Duration;
					float fadeTime = aSSplashScreen.FadeTime;
					_bootSplashTween.Append(aSSplashScreen.CanvasGroup.DOFade(1f, fadeTime));
					_bootSplashTween.AppendCallback(delegate
					{
						_isFadingBootSplash = false;
					});
					_bootSplashTween.AppendInterval(duration);
					_bootSplashTween.AppendCallback(delegate
					{
						_isFadingBootSplash = true;
					});
					_bootSplashTween.Append(aSSplashScreen.CanvasGroup.DOFade(0f, fadeTime));
					_bootSplashTween.AppendCallback(delegate
					{
						_currentBootSplashScreenIndex++;
					});
				}
				_bootSplashTween.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
				_bootSplashTween.OnComplete(TransitionToOpening);
				_bootSplashTween.Restart();
				_isFadingBootSplash = true;
			});
			State bootSplashes2 = _bootSplashes;
			bootSplashes2.onExit = (Action)Delegate.Combine(bootSplashes2.onExit, (Action)delegate
			{
				for (int i = 0; i < splashScreens.Length; i++)
				{
					splashScreens[i].CanvasGroup.interactable = false;
					splashScreens[i].CanvasGroup.blocksRaycasts = false;
				}
			});
			State opening = _opening;
			opening.onEnter = (Action)Delegate.Combine(opening.onEnter, (Action)delegate
			{
				FirstInitialization = false;
				_canSkipOpeningVideo = false;
				ControllerLifetime.OnControllerChanged += PointerManager.Instance.SetUIPointer;
				PointerManager.Instance.SetUIPointer();
				openingVideoPlayer.SetOnVideoEndCallback(TransitionToTitleMenu);
				openingVideoPlayer.StartVideo();
				_openingTween?.Kill();
				_openingTween = DOTween.Sequence(this);
				_openingTween.Append(openingVideoCanvasGroup.DOFade(1f, openingFadeTime));
				_openingTween.AppendInterval(allowOpeningSkipAfterSeconds);
				_openingTween.AppendCallback(delegate
				{
					_canSkipOpeningVideo = true;
				});
				_openingTween.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			});
			State opening2 = _opening;
			opening2.onExit = (Action)Delegate.Combine(opening2.onExit, (Action)delegate
			{
				_openingTween?.Kill();
				_openingTween = DOTween.Sequence(this);
				_openingTween.Append(openingVideoCanvasGroup.DOFade(0f, openingFadeTime));
				_openingTween.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			});
			State titleMenu = _titleMenu;
			titleMenu.onEnter = (Action)Delegate.Combine(titleMenu.onEnter, (Action)delegate
			{
				titleScreenMenuView.TransitionToTitleVideo();
			});
			State settingsMenu = _settingsMenu;
			settingsMenu.onEnter = (Action)Delegate.Combine(settingsMenu.onEnter, (Action)delegate
			{
				titleScreenMenuView.TransitionMainMenuToSettings();
				SettingsMenuController settingsMenuController = ControllerManager.Instance.OverrideGameController<SettingsMenuController>();
				settingsMenuController.Open();
				settingsMenuController.OnCloseOnce += TransitionToTitleMenu;
			});
			_settingsMenu.onExit = delegate
			{
				titleScreenMenuView.TransitionSettingsToMainMenu();
			};
			State creditsMenu = _creditsMenu;
			creditsMenu.onEnter = (Action)Delegate.Combine(creditsMenu.onEnter, (Action)delegate
			{
				titleScreenMenuView.TransitionMainMenuToCredits();
				CreditsMenuController creditsMenuController = ControllerManager.Instance.OverrideGameController<CreditsMenuController>();
				creditsMenuController.Open();
				creditsMenuController.OnCloseOnce += TransitionToTitleMenu;
			});
			State creditsMenu2 = _creditsMenu;
			creditsMenu2.onExit = (Action)Delegate.Combine(creditsMenu2.onExit, (Action)delegate
			{
				titleScreenMenuView.TransitionSettingsToMainMenu();
			});
			State end = _end;
			end.onEnter = (Action)Delegate.Combine(end.onEnter, (Action)delegate
			{
			});
			State quit = _quit;
			quit.onEnter = (Action)Delegate.Combine(quit.onEnter, (Action)delegate
			{
				titleScreenMenuView.EnableMenuInteraction(state: false);
				Application.Quit();
			});
			State continueGame = _continueGame;
			continueGame.onEnter = (Action)Delegate.Combine(continueGame.onEnter, (Action)delegate
			{
				SceneMaster.Instance.LoadScene(SceneEnum.Hub);
			});
			State newGame = _newGame;
			newGame.onEnter = (Action)Delegate.Combine(newGame.onEnter, (Action)delegate
			{
				SceneMaster.Instance.LoadScene(SceneEnum.PrologueVideo);
			});
		}

		private void TrySkipBootSplash()
		{
			if (_bootSplashTween == null || IsFadingBootSplash)
			{
				return;
			}
			_isFadingBootSplash = true;
			if (_currentBootSplashScreenIndex > splashScreens.Length - 1)
			{
				return;
			}
			float num = 0f;
			for (int i = 0; i <= _currentBootSplashScreenIndex; i++)
			{
				num += splashScreens[i].FadeTime + splashScreens[i].Duration;
				if (i < _currentBootSplashScreenIndex)
				{
					num += splashScreens[i].FadeTime;
				}
			}
			_bootSplashTween.Goto(num, andPlay: true);
		}

		private void TrySkipOpening()
		{
			_canSkipOpeningVideo = false;
			openingVideoPlayer.SkipVideo();
		}

		public void TransitionToSplashes()
		{
			_stateMachine?.MakeTransition(_bootSplashes);
		}

		public void TransitionToOpening()
		{
			_stateMachine?.MakeTransition(_opening);
		}

		public void TransitionToTitleMenu()
		{
			_stateMachine?.MakeTransition(_titleMenu);
		}

		public void TransitionToSettingsMenu()
		{
			_stateMachine?.MakeTransition(_settingsMenu);
		}

		public void TransitionToCreditsMenu()
		{
			_stateMachine?.MakeTransition(_creditsMenu);
		}

		public void TransitionToContinueGame()
		{
			_stateMachine?.MakeTransition(_continueGame);
		}

		public void TransitionToNewGame()
		{
			_stateMachine?.MakeTransition(_newGame);
		}

		public void TransitionToQuitGame()
		{
			_stateMachine?.MakeTransition(_quit);
		}

		private void Update()
		{
			_stateMachine?.UpdateTick();
		}

		public async void ContinueGame()
		{
			titleScreenMenuView.EnableMenuInteraction(state: false);
			if (SaveManager.HasSaveFiles())
			{
				await SaveManager.LoadGameFromSaveSlotAsync(0);
				TransitionToContinueGame();
			}
		}

		public void NewGame()
		{
			titleScreenMenuView.EnableMenuInteraction(state: false);
			LaunchNewGamePopup();
		}

		private void LaunchNewGamePopup()
		{
			string term = newGameWarningKey;
			LocalizationMediator.GetTranslation(ref term);
			if (SaveManager.HasSaveFiles())
			{
				PopupLauncher.Instance.RequestPopup(PopupLauncher.PopupType.SmallChoice, new PopupContext(term, (Action)delegate
				{
					ConfirmNewGame();
				}, (Action)delegate
				{
					titleScreenMenuView.EnableMenuInteraction(state: true);
					OnControllerTypeChanged();
				}));
			}
			else
			{
				ConfirmNewGame();
			}
			async UniTask ConfirmNewGame()
			{
				GameDataManager.Instance.ResetData();
				await GameDataManager.Instance.SaveGameData();
				TransitionToNewGame();
			}
		}

		public async void OpenSettings()
		{
			TransitionToSettingsMenu();
		}

		public void OpenCredits()
		{
			TransitionToCreditsMenu();
		}

		public async void QuitGame()
		{
			TransitionToQuitGame();
		}

		public override void UISubmit(InputActionEventData data)
		{
			if (data.eventType == InputActionEventType.ButtonJustPressed && _stateMachine.GetState() == _titleMenu && titleScreenMenuView.IsTitleSplashInteractable)
			{
				titleScreenMenuView.ExecuteSplashButtonOnClick();
			}
		}

		public override void UIDirectionalLeft(InputActionEventData data)
		{
			if (titleScreenMenuView.IsMainMenuInteractable && data.eventType == InputActionEventType.NegativeButtonJustPressed)
			{
				this.OnUIDirectionalLeftPressed?.Invoke();
			}
		}

		public override void UIDirectionalRight(InputActionEventData data)
		{
			if (titleScreenMenuView.IsMainMenuInteractable && data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnUIDirectionalRightPressed?.Invoke();
			}
		}

		public override void UIDirectionalUp(InputActionEventData data)
		{
			if (titleScreenMenuView.IsMainMenuInteractable && data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnUIDirectionalUpPressed?.Invoke();
			}
		}

		public override void UIDirectionalDown(InputActionEventData data)
		{
			if (titleScreenMenuView.IsMainMenuInteractable && data.eventType == InputActionEventType.NegativeButtonJustPressed)
			{
				this.OnUIDirectionalDownPressed?.Invoke();
			}
		}

		public override void AnyInputDown()
		{
			if (Application.isFocused)
			{
				if (_stateMachine.GetState() == _bootSplashes)
				{
					TrySkipBootSplash();
				}
				if (_stateMachine.GetState() == _opening && _canSkipOpeningVideo)
				{
					TrySkipOpening();
				}
				if (_stateMachine.GetState() == _titleMenu)
				{
					titleScreenMenuView.TrySkipToSplashAnimation();
				}
			}
		}

		public override void AnyMouseInputStateChanged(int button, bool pressed)
		{
			if (Application.isFocused && pressed && button == 4)
			{
				if (_stateMachine.GetState() == _bootSplashes)
				{
					TrySkipBootSplash();
				}
				if (_stateMachine.GetState() == _opening && _canSkipOpeningVideo)
				{
					TrySkipOpening();
				}
				if (_stateMachine.GetState() == _titleMenu)
				{
					titleScreenMenuView.TrySkipToSplashAnimation();
				}
			}
		}

		public virtual void OnControllerTypeChanged()
		{
			if (ControllerLifetime.ActiveControllerType != ControllerType.Mouse)
			{
				if (_stateMachine.GetState() == _titleMenu && titleScreenMenuView.IsMainMenuInteractable)
				{
					titleScreenMenuView.SelectFocusedMainButton();
				}
			}
			else if (_stateMachine.GetState() == _titleMenu && titleScreenMenuView.IsMainMenuInteractable)
			{
				EventSystem.current.SetSelectedGameObject(null);
			}
		}

		public void OnApplicationFocus(bool hasFocus)
		{
			if (hasFocus)
			{
				OnControllerTypeChanged();
			}
		}
	}
}
