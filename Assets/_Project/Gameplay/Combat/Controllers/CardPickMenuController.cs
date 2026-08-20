using System;
using AstralShift.Control;
using AstralShift.Control.Controllers;
using AstralShift.HSM;
using AstralShift.HellMaiden.Audio;
using AstralShift.HellMaiden.UI;
using AstralShift.HellMaiden.UI.Cards;
using AstralShift.HellMaiden.UI.Menus;
using AstralShift.HellMaiden.UI.Menus.Hand;
using AstralShift.Managers;
using Cysharp.Threading.Tasks;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.HellMaiden.Controllers
{
	public class CardPickMenuController : UIController
	{
		[SerializeField]
		public UICardPickMenuView menuView;

		[SerializeField]
		private float confirmHandHoldTime = 0.2f;

		[SerializeField]
		private float reRollCardHoldTime = 2f;

		[SerializeField]
		private float discardCardHoldTime = 2f;

		[SerializeField]
		private float banishCardHoldTime = 2f;

		[SerializeField]
		private float onSuspensionDelay = 0.5f;

		[SerializeField]
		private UICardPickFocusMenuElementMouseHandler[] focusElementsMouseHandlers;

		private StateMachine _mainStateMachine;

		private StateNode _opening;

		private StateNode _waitingForPick;

		private StateNode _sortingDeck;

		private StateNode _reRolling;

		private StateNode _closing;

		private StateNode _suspended;

		private StateNode _noSelection;

		private StateNode _cardSelected;

		private StateNode _draggingCardToHand;

		private StateNode _swappingCardInHand;

		private StateNode _handSlotSelected;

		private StateNode _swappingHandSlot;

		private StateNode _mergingCards;

		private UICardViewHandler _currentSelectedCard;

		public UICardPickMenuView MenuView => menuView;

		public float ConfirmHandHoldTime => confirmHandHoldTime;

		public float ReRollCardHoldTime => reRollCardHoldTime;

		public float DiscardCardHoldTime => discardCardHoldTime;

		public float BanishCardHoldTime => banishCardHoldTime;

		public StateMachine MainStateMachine => _mainStateMachine;

		public bool IsOpening
		{
			get
			{
				if (_mainStateMachine != null)
				{
					return _mainStateMachine.CurrentState == _opening;
				}
				return false;
			}
		}

		public bool IsClosing
		{
			get
			{
				if (_mainStateMachine != null)
				{
					return _mainStateMachine.CurrentState == _closing;
				}
				return false;
			}
		}

		public bool IsActive
		{
			get
			{
				if (_mainStateMachine != null && _mainStateMachine.CurrentState != _opening && _mainStateMachine.CurrentState != _closing)
				{
					return menuView.IsInteractable;
				}
				return false;
			}
		}

		public bool IsWaitingForPick
		{
			get
			{
				if (_mainStateMachine != null)
				{
					if (_mainStateMachine.CurrentState != _waitingForPick)
					{
						return _mainStateMachine.CurrentState.Parent == _waitingForPick;
					}
					return true;
				}
				return false;
			}
		}

		public bool IsSuspended
		{
			get
			{
				if (_mainStateMachine != null)
				{
					return _mainStateMachine.CurrentState == _suspended;
				}
				return false;
			}
		}

		public bool IsSortingDeck
		{
			get
			{
				if (_mainStateMachine != null)
				{
					return _mainStateMachine.CurrentState == _sortingDeck;
				}
				return false;
			}
		}

		public bool IsDraggingCard
		{
			get
			{
				if (_mainStateMachine != null)
				{
					return _mainStateMachine.CurrentState == _draggingCardToHand;
				}
				return false;
			}
		}

		public bool IsSwappingCard
		{
			get
			{
				if (_mainStateMachine != null)
				{
					return _mainStateMachine.CurrentState == _swappingCardInHand;
				}
				return false;
			}
		}

		public bool IsCardSelected
		{
			get
			{
				if (_mainStateMachine != null)
				{
					return _mainStateMachine.CurrentState == _cardSelected;
				}
				return false;
			}
		}

		public bool IsNoSelection
		{
			get
			{
				if (_mainStateMachine != null)
				{
					return _mainStateMachine.CurrentState == _noSelection;
				}
				return false;
			}
		}

		public bool IsHandSlotSelected
		{
			get
			{
				if (_mainStateMachine != null)
				{
					return _mainStateMachine.CurrentState == _handSlotSelected;
				}
				return false;
			}
		}

		public bool IsSwappingHandSlot
		{
			get
			{
				if (_mainStateMachine != null)
				{
					return _mainStateMachine.CurrentState == _swappingHandSlot;
				}
				return false;
			}
		}

		public bool IsMergingCards
		{
			get
			{
				if (_mainStateMachine != null)
				{
					return _mainStateMachine.CurrentState == _mergingCards;
				}
				return false;
			}
		}

		public bool IsFirstInteraction { get; set; } = true;

		public event Func<UniTask> OnUIDirectionalLeftPressed;

		public event Func<UniTask> OnUIDirectionalRightPressed;

		public event Action OnUIDirectionalDownPressed;

		public event Action OnUIDirectionalUpPressed;

		public event Action<InputActionEventData> OnUIRightAnalogHorizontal;

		public event Action<InputActionEventData> OnUIRightAnalogVertical;

		public event Action OnUISubmitPressed;

		public event Action OnUICancelPressed;

		public event Action OnUICancelReleased;

		public event Action OnUIButton3Pressed;

		public event Action OnUIButton3Released;

		public event Action OnUIButton4Pressed;

		public event Action OnUIButton4Released;

		public event Action OnUICenter1Pressed;

		public event Action<float> OnUICenter2Hold;

		public event Action<ControllerType> OnBeforeControllerTypeChangeCallback;

		public event Action OnControllerTypeChangeCallback;

		public override void Activate()
		{
			base.Activate();
			ReInput.configuration.ignoreInputWhenAppNotInFocus = false;
			MusicPlayer.Instance.SetSnapShot(MusicPlayer.SnapshotID.Card);
			ControllerLifetime.UnifiedEventSystem = true;
			ControllerLifetime.OnBeforeControllerChanged += OnBeforeControllerTypeChange;
			ControllerLifetime.OnControllerChanged += PointerManager.Instance.SetUIPointer;
			ControllerLifetime.OnControllerChanged += OnControllerTypeChange;
			PointerManager.Instance.SetUIPointer();
			PauseManager.Instance.PauseGame();
			if (IsSuspended)
			{
				ReturnFromSuspended().Forget();
			}
			else if (_mainStateMachine == null)
			{
				InitializeStateMachinePipeline();
			}
			else
			{
				TransitionToOpen();
			}
		}

		public override void Deactivate()
		{
			base.Deactivate();
			ReInput.configuration.ignoreInputWhenAppNotInFocus = true;
			ControllerLifetime.UnifiedEventSystem = false;
			ControllerLifetime.OnBeforeControllerChanged -= OnBeforeControllerTypeChange;
			ControllerLifetime.OnControllerChanged -= PointerManager.Instance.SetUIPointer;
			ControllerLifetime.OnControllerChanged -= OnControllerTypeChange;
			PauseManager.Instance.ResumeGame();
		}

		public void OnDestroy()
		{
			ControllerManager.Instance.UnSubscribe(this);
		}

		private void InitializeStateMachinePipeline()
		{
			_mainStateMachine = new StateMachine("Card Pick Menu");
			_mainStateMachine.CreateState("Opening", out _opening).SetAsInitial().SetOnEnter(delegate
			{
				CPMCardViewInputHandler.GlobalReleaseLock();
				_currentSelectedCard = null;
				ClearBindings();
				menuView.EnableMenuInteraction(state: false);
				EnableFocusElementsMouseHandlers(state: true);
				IsFirstInteraction = true;
				return UniTask.CompletedTask;
			})
				.SetOnExit(delegate
				{
					EnableFocusElementsMouseHandlers(ControllerLifetime.ActiveControllerType == ControllerType.Mouse);
					return UniTask.CompletedTask;
				});
			_mainStateMachine.CreateState("Waiting For Pick", out _waitingForPick).SetOnEnter(delegate
			{
				ConstructOfferingsNavigation();
				menuView.HandView.ConstructHandNavigation();
				menuView.EnableMenuInteraction(state: true);
				MenuView.SetFocusOnOfferings(instant: true);
				ApplyFirstSelection();
				RegisterCycleFocusGroupsBindings();
				RegisterDeckSortBinding();
				if (!menuView.IsWeaponDropLayout)
				{
					menuView.RegisterSkipBindings();
				}
				MenuView.EnableContextualGlyphs(state: true);
				MenuView.ShowSlotSwapModeAcceptGlyph(state: true);
				MenuView.ShowDetailsGlyph(state: true);
				OnUICenter1Pressed += OpenDetailsMenu;
				return UniTask.CompletedTask;
			}).SetOnExit(delegate
			{
				MenuView.ShowSlotSwapModeAcceptGlyph(state: false);
				MenuView.EnableContextualGlyphs(state: false);
				UnRegisterCycleFocusGroupsBindings();
				UnRegisterDeckSortingBinding();
				menuView.UnRegisterSkipBindings();
				OnUICenter1Pressed -= OpenDetailsMenu;
				return UniTask.CompletedTask;
			})
				.AddSubState("No Selection", out _noSelection, delegate(StateBuilder noSelect)
				{
					noSelect.SetAsInitial();
				})
				.AddSubState("Card Selected", out _cardSelected)
				.AddSubState("Dragging Card", out _draggingCardToHand, delegate(StateBuilder drag)
				{
					drag.SetOnEnter(delegate
					{
						MenuView.ShowSlotSwapModeAcceptGlyph(state: false);
						MenuView.ShowDetailsGlyph(state: false);
						return UniTask.CompletedTask;
					}).SetOnExit(delegate
					{
						MenuView.ShowSlotSwapModeAcceptGlyph(state: true);
						MenuView.ShowDetailsGlyph(state: true);
						return UniTask.CompletedTask;
					});
				})
				.AddSubState("Swapping Card", out _swappingCardInHand, delegate(StateBuilder swapCard)
				{
					swapCard.SetOnEnter(delegate
					{
						MenuView.ShowSlotSwapModeAcceptGlyph(state: false);
						MenuView.ShowDetailsGlyph(state: false);
						MenuView.LockFocusOnHandGroup();
						return UniTask.CompletedTask;
					}).SetOnExit(delegate
					{
						MenuView.ShowSlotSwapModeAcceptGlyph(state: true);
						MenuView.ShowDetailsGlyph(state: true);
						MenuView.ResetPermanentFocus();
						return UniTask.CompletedTask;
					});
				})
				.AddSubState("Hand Slot Selected", out _handSlotSelected, delegate(StateBuilder handSlot)
				{
					handSlot.SetOnEnter(delegate
					{
						MenuView.ShowSlotSwapModeAcceptGlyph(state: false);
						MenuView.ShowSlotSwapModeCancelGlyph(state: true);
						MenuView.ShowDetailsGlyph(state: false);
						return UniTask.CompletedTask;
					}).SetOnExit(delegate
					{
						MenuView.ShowSlotSwapModeAcceptGlyph(state: true);
						MenuView.ShowSlotSwapModeCancelGlyph(state: false);
						MenuView.ShowDetailsGlyph(state: false);
						return UniTask.CompletedTask;
					});
				})
				.AddSubState("Swapping Hand Slot", out _swappingHandSlot);
			_mainStateMachine.CreateState("Merging Cards", out _mergingCards).SetOnEnter(delegate
			{
				MenuView.EnableContextualGlyphs(state: false);
				MenuView.ShowDetailsGlyph(state: false);
				MenuView.UnRegisterSkipBindings();
				return UniTask.CompletedTask;
			});
			_mainStateMachine.CreateState("Sorting Deck", out _sortingDeck).SetOnEnter(delegate
			{
				UnRegisterDeckSortingBinding();
				UnRegisterCycleFocusGroupsBindings();
				MenuView.UnRegisterSkipBindings();
				MenuView.EnableReRoll(state: false);
				PlayerHandSlotView firstHandSlotWithWeapons = MenuView.HandView.GetFirstHandSlotWithWeapons();
				EventSystem.current.SetSelectedGameObject(null);
				MenuView.EnableMenuInteraction(state: false);
				_mainStateMachine.MakeTransitionAsync(_handSlotSelected).Forget();
				MenuView.HandView.FoldHand(instant: true).Forget();
				MenuView.SetFocusOnHand(instant: true);
				MenuView.EnableContextualGlyphs(state: false);
				MenuView.EnableContextualGlyphs(state: true);
				MenuView.ShowSlotSwapModeCancelGlyph(state: true);
				MenuView.EnableMenuInteraction(state: true);
				EventSystem.current.SetSelectedGameObject(firstHandSlotWithWeapons.gameObject);
				OnUIButton3Pressed += ExitSortDeckMode;
				return UniTask.CompletedTask;
			}).SetOnExit(delegate
			{
				EventSystem.current.SetSelectedGameObject(null);
				MenuView.EnableMenuInteraction(state: false);
				OnUIButton3Pressed -= ExitSortDeckMode;
				this.OnUISubmitPressed = null;
				this.OnUICancelPressed = null;
				this.OnUIDirectionalLeftPressed = null;
				this.OnUIDirectionalRightPressed = null;
				MenuView.HandView.HideSortDeckSelectionVFX();
				MenuView.HandView.ResetSwapModePositions();
				MenuView.EnableContextualGlyphs(state: false);
				return UniTask.CompletedTask;
			});
			_mainStateMachine.CreateState("ReRolling", out _reRolling).SetOnEnter(delegate
			{
				IsFirstInteraction = true;
				return UniTask.CompletedTask;
			});
			_mainStateMachine.CreateState("Closing", out _closing).SetOnEnter(delegate
			{
				EventSystem.current.SetSelectedGameObject(null);
				MenuView.EnableMenuInteraction(state: false);
				MenuView.ShowDetailsGlyph(state: false);
				MenuView.ShowSlotSwapModeAcceptGlyph(state: false);
				MenuView.EnableContextualGlyphs(state: false);
				return UniTask.CompletedTask;
			});
			_mainStateMachine.CreateState("Suspended", out _suspended).SetOnEnter(delegate
			{
				EventSystem.current.SetSelectedGameObject(null);
				return UniTask.CompletedTask;
			});
			_mainStateMachine.CreateTransition(_opening, _waitingForPick);
			_mainStateMachine.CreateTransition(_waitingForPick, _closing);
			_mainStateMachine.CreateTransition(_closing, _opening);
			_mainStateMachine.CreateTransition(_waitingForPick, _sortingDeck);
			_mainStateMachine.CreateTransition(_sortingDeck, _waitingForPick);
			_mainStateMachine.CreateTransition(_waitingForPick, _reRolling);
			_mainStateMachine.CreateTransition(_reRolling, _waitingForPick);
			_mainStateMachine.CreateTransition(_waitingForPick, _suspended);
			_mainStateMachine.CreateTransition(_suspended, _waitingForPick);
			_mainStateMachine.CreateTransition(_mergingCards, _waitingForPick);
			_mainStateMachine.CreateTransition(_mergingCards, _closing);
			_mainStateMachine.CreateTransition(_waitingForPick, _mergingCards);
			_mainStateMachine.CreateTransition(_noSelection, _cardSelected);
			_mainStateMachine.CreateTransition(_cardSelected, _noSelection);
			_mainStateMachine.CreateTransition(_cardSelected, _draggingCardToHand);
			_mainStateMachine.CreateTransition(_noSelection, _draggingCardToHand);
			_mainStateMachine.CreateTransition(_draggingCardToHand, _noSelection);
			_mainStateMachine.CreateTransition(_cardSelected, _swappingCardInHand);
			_mainStateMachine.CreateTransition(_noSelection, _swappingCardInHand);
			_mainStateMachine.CreateTransition(_swappingCardInHand, _cardSelected);
			_mainStateMachine.CreateTransition(_swappingCardInHand, _noSelection);
			_mainStateMachine.CreateTransition(_noSelection, _handSlotSelected);
			_mainStateMachine.CreateTransition(_handSlotSelected, _noSelection);
			_mainStateMachine.CreateTransition(_cardSelected, _handSlotSelected);
			_mainStateMachine.CreateTransition(_handSlotSelected, _cardSelected);
			_mainStateMachine.CreateTransition(_handSlotSelected, _swappingHandSlot);
			_mainStateMachine.CreateTransition(_swappingHandSlot, _handSlotSelected);
			_mainStateMachine.SetInitialState(_opening);
		}

		public void TransitionToOpen()
		{
			_mainStateMachine.MakeTransitionAsync(_opening).Forget();
		}

		public void TransitionToWaitingPick()
		{
			_mainStateMachine.MakeTransitionAsync(_waitingForPick).Forget();
		}

		public void TransitionToReRolling()
		{
			_mainStateMachine.MakeTransitionAsync(_reRolling).Forget();
		}

		public void TransitionToClose()
		{
			_mainStateMachine.MakeTransitionAsync(_closing).Forget();
		}

		public void TransitionToCardSelected(UICardViewHandler cardViewHandler)
		{
			_currentSelectedCard = cardViewHandler;
			_mainStateMachine.MakeTransitionAsync(_cardSelected).Forget();
		}

		public void TransitionToDraggingCardToHand()
		{
			_mainStateMachine.MakeTransitionAsync(_draggingCardToHand).Forget();
		}

		public void TransitionToSwappingCard()
		{
			_mainStateMachine.MakeTransitionAsync(_swappingCardInHand).Forget();
		}

		public void TransitionToHandSlotSelected()
		{
			_mainStateMachine.MakeTransitionAsync(_handSlotSelected).Forget();
		}

		public void TransitionToMergingCards()
		{
			_mainStateMachine.MakeTransitionAsync(_mergingCards).Forget();
		}

		public void TransitionToNoSelection()
		{
			_currentSelectedCard = null;
			_mainStateMachine?.MakeTransitionAsync(_noSelection).Forget();
		}

		public void TransitionToSuspended()
		{
			_mainStateMachine?.MakeTransitionAsync(_suspended).Forget();
		}

		private async UniTaskVoid ReturnFromSuspended()
		{
			await UniTask.Delay((int)(onSuspensionDelay * 1000f), ignoreTimeScale: true);
			TransitionToWaitingPick();
		}

		public override async void UICenter1(InputActionEventData data)
		{
			if (IsActive && data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnUICenter1Pressed?.Invoke();
			}
		}

		public override void UICenter2(InputActionEventData data)
		{
			if (menuView.IsInteractable)
			{
				this.OnUICenter2Hold?.Invoke((float)data.GetButtonTimePressed());
			}
		}

		public override void UISubmit(InputActionEventData data)
		{
			if (menuView.IsInteractable && data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnUISubmitPressed?.Invoke();
			}
		}

		public override void UICancel(InputActionEventData data)
		{
			if (data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnUICancelPressed?.Invoke();
			}
			else if (data.eventType == InputActionEventType.ButtonJustReleased)
			{
				this.OnUICancelReleased?.Invoke();
			}
		}

		public override void UIButton3(InputActionEventData data)
		{
			if (data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnUIButton3Pressed?.Invoke();
			}
			else if (data.eventType == InputActionEventType.ButtonJustReleased)
			{
				this.OnUIButton3Released?.Invoke();
			}
		}

		public override void UIButton4(InputActionEventData data)
		{
			if (data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnUIButton4Pressed?.Invoke();
			}
			else if (data.eventType == InputActionEventType.ButtonJustReleased)
			{
				this.OnUIButton4Released?.Invoke();
			}
		}

		public override void UIDirectionalUp(InputActionEventData data)
		{
			if (IsActive && data.eventType == InputActionEventType.ButtonRepeating)
			{
				this.OnUIDirectionalUpPressed?.Invoke();
			}
		}

		public override void UIDirectionalDown(InputActionEventData data)
		{
			if (IsActive && data.eventType == InputActionEventType.NegativeButtonRepeating)
			{
				this.OnUIDirectionalDownPressed?.Invoke();
			}
		}

		public override void UIDirectionalLeft(InputActionEventData data)
		{
			if (menuView.IsInteractable && data.eventType == InputActionEventType.NegativeButtonJustPressed)
			{
				this.OnUIDirectionalLeftPressed?.Invoke();
			}
		}

		public override void UIDirectionalRight(InputActionEventData data)
		{
			if (IsActive && data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnUIDirectionalRightPressed?.Invoke();
			}
		}

		public override void UIRightStickHorizontal(InputActionEventData data)
		{
			if (IsActive)
			{
				this.OnUIRightAnalogHorizontal?.Invoke(data);
			}
		}

		public override void UIRightStickVertical(InputActionEventData data)
		{
			if (IsActive)
			{
				this.OnUIRightAnalogVertical?.Invoke(data);
			}
		}

		private void RegisterDeckSortBinding()
		{
			OnUIButton3Pressed += EnterSortDeckMode;
		}

		private void UnRegisterDeckSortingBinding()
		{
			OnUIButton3Pressed -= EnterSortDeckMode;
		}

		private void RegisterCycleFocusGroupsBindings()
		{
			OnUIDirectionalUpPressed += CycleFocusGroups;
			OnUIDirectionalDownPressed += CycleFocusGroups;
		}

		private void UnRegisterCycleFocusGroupsBindings()
		{
			OnUIDirectionalUpPressed -= CycleFocusGroups;
			OnUIDirectionalDownPressed -= CycleFocusGroups;
		}

		private void OpenDetailsMenu()
		{
			if (IsActive && !IsDraggingCard && !IsSwappingCard && CombatUIManager.Instance != null)
			{
				TransitionToSuspended();
				CombatUIManager.Instance.OpenStatsMenu(0, instant: true);
			}
		}

		private void ClearBindings()
		{
			this.OnUISubmitPressed = null;
			this.OnUICancelPressed = null;
			this.OnUICancelReleased = null;
			this.OnUIButton3Pressed = null;
			this.OnUIButton3Released = null;
			this.OnUIButton4Pressed = null;
			this.OnUIButton4Released = null;
			this.OnUIDirectionalLeftPressed = null;
			this.OnUIDirectionalRightPressed = null;
			this.OnUIDirectionalUpPressed = null;
			this.OnUIDirectionalDownPressed = null;
			this.OnUIRightAnalogHorizontal = null;
			this.OnUIRightAnalogVertical = null;
			this.OnUICenter2Hold = null;
		}

		private void OnBeforeControllerTypeChange(ControllerType controllerType)
		{
			if (IsActive && !IsMergingCards)
			{
				this.OnBeforeControllerTypeChangeCallback?.Invoke(controllerType);
				if (!IsDraggingCard && !IsSwappingCard)
				{
					EnableFocusElementsMouseHandlers(controllerType == ControllerType.Mouse);
				}
			}
		}

		private void EnableFocusElementsMouseHandlers(bool state)
		{
			UICardPickFocusMenuElementMouseHandler[] array = focusElementsMouseHandlers;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].enabled = state;
			}
		}

		public void OnControllerTypeChange()
		{
			if (!IsActive || IsMergingCards)
			{
				return;
			}
			if (ControllerLifetime.ActiveControllerType == ControllerType.Mouse)
			{
				if (!IsDraggingCard && !IsSwappingCard && IsSortingDeck)
				{
					ExitSortDeckMode();
				}
			}
			else if (ControllerLifetime.LastActiveControllerType == ControllerType.Mouse && !IsDraggingCard)
			{
				if (IsSortingDeck)
				{
					SelectFirstSlotIfFound();
					return;
				}
				ConstructOfferingsNavigation();
				menuView.HandView.ConstructHandNavigation();
				menuView.SetFocusOnOfferings(instant: true);
				SelectFirstCardInOfferings();
			}
		}

		public void ApplyFirstSelection()
		{
			SelectFirstCardInOfferings();
		}

		private void CycleFocusGroups()
		{
			if (!IsMergingCards && !IsSwappingCard && (IsCardSelected || IsNoSelection || IsHandSlotSelected))
			{
				EnableFocusElementsMouseHandlers(state: false);
				UnRegisterCycleFocusGroupsBindings();
				if (menuView.SelectedFocusElement == menuView.HandFocus)
				{
					menuView.SetFocusOnOfferings(instant: true);
					SelectFirstCardInOfferings();
				}
				else
				{
					menuView.SetFocusOnHand(instant: true);
					MenuView.HandView.ConstructHandNavigation();
					SelectFirstCardInHand();
				}
				RegisterCycleFocusGroupsBindings();
				if (ControllerLifetime.ActiveControllerType == ControllerType.Mouse)
				{
					EnableFocusElementsMouseHandlers(state: true);
				}
			}
		}

		private async void SelectFirstCardInOfferings()
		{
			if (menuView.CardsInOfferings.Count != 0)
			{
				if (menuView.CardsInOfferings.Contains(_currentSelectedCard))
				{
					EventSystem.current.SetSelectedGameObject(_currentSelectedCard.gameObject);
					TransitionToCardSelected(_currentSelectedCard);
				}
				else
				{
					EventSystem.current.SetSelectedGameObject(menuView.CardsInOfferings[0].gameObject);
					TransitionToCardSelected(menuView.CardsInOfferings[0]);
				}
			}
		}

		private void SelectFirstCardInHand()
		{
			PlayerHandSlotView firstHandSlotWithEquipments = menuView.HandView.GetFirstHandSlotWithEquipments();
			if ((bool)firstHandSlotWithEquipments)
			{
				UICardViewHandler firstFoundEquipmentView = firstHandSlotWithEquipments.GetFirstFoundEquipmentView();
				if ((bool)firstFoundEquipmentView)
				{
					EventSystem.current.SetSelectedGameObject(firstFoundEquipmentView.gameObject);
					TransitionToCardSelected(firstFoundEquipmentView);
				}
			}
		}

		private void SelectFirstSlotIfFound()
		{
			PlayerHandSlotView firstHandSlotWithWeapons = MenuView.HandView.GetFirstHandSlotWithWeapons();
			if ((bool)firstHandSlotWithWeapons)
			{
				EventSystem.current.SetSelectedGameObject(firstHandSlotWithWeapons.gameObject);
			}
		}

		private void EnterSortDeckMode()
		{
			if (IsActive && !IsSwappingCard && !IsDraggingCard && !IsMergingCards && (bool)MenuView.HandView.GetFirstHandSlotWithWeapons())
			{
				_mainStateMachine.MakeTransitionAsync(_sortingDeck).Forget();
			}
		}

		private void ExitSortDeckMode()
		{
			_mainStateMachine.MakeTransitionAsync(_waitingForPick).Forget();
		}

		private void ConstructOfferingsNavigation()
		{
			for (int i = 0; i < menuView.CardsInOfferings.Count; i++)
			{
				menuView.CardsInOfferings[i].InputHandler.ClearNavigation();
			}
			if (menuView.CardsInOfferings.Count > 1)
			{
				for (int j = 1; j < menuView.CardsInOfferings.Count; j++)
				{
					menuView.CardsInOfferings[j - 1].InputHandler.SetRightNavigation(menuView.CardsInOfferings[j].InputHandler);
					menuView.CardsInOfferings[j].InputHandler.SetLeftNavigation(menuView.CardsInOfferings[j - 1].InputHandler);
				}
			}
		}
	}
}
