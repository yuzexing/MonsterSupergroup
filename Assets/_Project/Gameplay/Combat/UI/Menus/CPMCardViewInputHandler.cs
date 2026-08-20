using System;
using System.Threading;
using AstralShift.FSM;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.UI.Cards;
using AstralShift.HellMaiden.UI.Menus.Hand;
using Cysharp.Threading.Tasks;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.HellMaiden.UI.Menus
{
	public class CPMCardViewInputHandler : UICardViewInputHandler
	{
		private CardPickMenuController _menuController;

		protected PlayerHandSlotView _beforeSwapHandSlot;

		protected PlayerHandSlotView _selectedHandSlot;

		private StateMachine _stateMachine;

		private State _selectedState;

		private State _unSelectedState;

		private State _droppingToHandState;

		private State _swappingInHandState;

		private CancellationTokenSource _activeModeCTS;

		private bool _isHoldingReRoll;

		private bool _isHoldingDiscard;

		private bool _isHoldingBanish;

		private bool _isPointerOver;

		private bool _firstInteraction = true;

		private bool PreventLockSpam;

		private bool _isLocking;

		public bool IsDroppingToHand
		{
			get
			{
				if (_stateMachine != null)
				{
					return _stateMachine.GetState() == _droppingToHandState;
				}
				return false;
			}
		}

		public bool IsSwappingInHand
		{
			get
			{
				if (_stateMachine != null)
				{
					return _stateMachine.GetState() == _swappingInHandState;
				}
				return false;
			}
		}

		public bool IsLocking
		{
			get
			{
				if (PreventLockSpam)
				{
					return _isLocking;
				}
				return false;
			}
			set
			{
				_isLocking = value;
			}
		}

		private bool IsMouseInViewport
		{
			get
			{
				if (Input.mousePosition.x > 0f && Input.mousePosition.x < (float)Screen.width && Input.mousePosition.y > 0f)
				{
					return Input.mousePosition.y < (float)Screen.height;
				}
				return false;
			}
		}

		private static CPMCardViewInputHandler ActiveHandler { get; set; }

		protected override void Awake()
		{
			base.Awake();
			if (Application.isPlaying)
			{
				if (!_menuController)
				{
					_menuController = UICardPickMenuView.Instance.Controller;
					_menuController.OnBeforeControllerTypeChangeCallback += OnBeforeControllerTypeChange;
				}
				_stateMachine = new StateMachine("CPMCardViewGamepadHandler");
				_selectedState = new State("Selected");
				_unSelectedState = new State("Unselected");
				_droppingToHandState = new State("Dragging");
				_swappingInHandState = new State("Swapping");
				_stateMachine.AddTransition(_selectedState, _unSelectedState);
				_stateMachine.AddTransition(_unSelectedState, _selectedState);
				_stateMachine.AddTransition(_selectedState, _droppingToHandState);
				_stateMachine.AddTransition(_selectedState, _swappingInHandState);
				_stateMachine.AddTransition(_droppingToHandState, _selectedState);
				_stateMachine.AddTransition(_swappingInHandState, _selectedState);
				_stateMachine.AddTransition(_unSelectedState, _droppingToHandState);
				_stateMachine.AddTransition(_unSelectedState, _swappingInHandState);
				_stateMachine.AddTransition(_droppingToHandState, _unSelectedState);
				_stateMachine.AddTransition(_swappingInHandState, _unSelectedState);
				_stateMachine.SetInitialStateNoCallbacks(_unSelectedState);
			}
		}

		private void OnBeforeControllerTypeChange(ControllerType controllerType)
		{
			if (controllerType == ControllerType.Mouse)
			{
				CancelActions();
				base.CurrentType = controllerType;
			}
		}

		public override void ClearBindings()
		{
			UnRegisterBanishBindings();
			UnRegisterDiscardBindings();
			UnRegisterDropToHandBindings();
			UnRegisterSwapHandBindings();
			UnRegisterTiltBindings();
		}

		protected override void OnDestroy()
		{
			if (Application.isPlaying)
			{
				if (ActiveHandler == this)
				{
					ActiveHandler = null;
				}
				if ((bool)_menuController)
				{
					_menuController.OnBeforeControllerTypeChangeCallback -= OnBeforeControllerTypeChange;
				}
				ClearBindings();
				base.OnDestroy();
			}
		}

		public override void CancelActions()
		{
			if (IsLocking || _menuController.IsMergingCards)
			{
				return;
			}
			_isHoldingReRoll = false;
			_isHoldingDiscard = false;
			_isHoldingBanish = false;
			bool flag = IsDroppingToHand || IsSwappingInHand;
			if (IsDroppingToHand)
			{
				CancelDrop();
			}
			else if (IsSwappingInHand)
			{
				CancelSwap();
			}
			PointerEventData eventData = new PointerEventData(EventSystem.current);
			if (viewHandler.IsDragging)
			{
				if (flag)
				{
					_menuController.MenuView.EnableReRoll(state: false);
					_menuController.MenuView.EnableDiscard(state: false);
					_menuController.MenuView.EnableBanish(state: false);
				}
				else
				{
					OnEndDrag(eventData);
				}
			}
			else
			{
				OnPointerExit(eventData);
			}
			_activeModeCTS?.Cancel();
			_activeModeCTS?.Dispose();
			_activeModeCTS = null;
		}

		protected override void OnDisable()
		{
			base.OnDisable();
			if (Application.isPlaying)
			{
				IsLocking = false;
				CancelActions();
				ReleaseLock();
				if (!_menuController || !_menuController.IsMergingCards)
				{
					UnRegisterReRollBindings();
					UnRegisterDiscardBindings();
					UnRegisterBanishBindings();
					StopGamepadDiscard();
					StopGamepadBanish();
					StopGamepadReRoll();
				}
			}
		}

		private void ResetActiveModeCts()
		{
			_activeModeCTS?.Cancel();
			_activeModeCTS?.Dispose();
			_activeModeCTS = new CancellationTokenSource();
		}

		private bool TryClaimLock()
		{
			if (ActiveHandler != null && ActiveHandler != this)
			{
				if (ActiveHandler.IsLocking)
				{
					return false;
				}
				ActiveHandler.ForceDeselect();
			}
			ActiveHandler = this;
			return true;
		}

		private void ReleaseLock()
		{
			if (ActiveHandler == this)
			{
				ActiveHandler = null;
			}
		}

		public static void GlobalReleaseLock()
		{
			ActiveHandler?.ForceDeselect();
			ActiveHandler = null;
		}

		private void ForceDeselect()
		{
			if (_stateMachine != null && _stateMachine.GetState() != _unSelectedState)
			{
				viewHandler.CardView.EnableSelectionOuterGlow(state: false);
				viewHandler.UnSelect();
				if (viewHandler.IsIdle)
				{
					UnRegisterTiltBindings();
					viewHandler.CardView.UnHover();
					viewHandler.CardView.DisableTilt();
					viewHandler.CardView.EnableIdleAnimation(state: true);
					UnRegisterReRollBindings();
					UnRegisterBanishBindings();
				}
				else
				{
					UnRegisterDiscardBindings();
				}
				TransitionToUnSelectedState();
				viewHandler.CardView.DisableTilt();
				_menuController.MenuView.EnableDiscard(state: false);
				_menuController.MenuView.EnableDiscardGlyph(state: false);
			}
		}

		private void TransitionToSelectedState()
		{
			_stateMachine.MakeTransition(_selectedState);
			_menuController.TransitionToCardSelected(viewHandler);
		}

		private void TransitionToUnSelectedState()
		{
			_stateMachine.MakeTransition(_unSelectedState);
			_menuController.TransitionToNoSelection();
			ReleaseLock();
		}

		private void TransitionToDraggingState()
		{
			_stateMachine.MakeTransition(_droppingToHandState);
		}

		private void TransitionToSwappingState()
		{
			_stateMachine.MakeTransition(_swappingInHandState);
		}

		public override void OnSelect(BaseEventData eventData)
		{
			if (_menuController.IsMergingCards || !TryClaimLock())
			{
				return;
			}
			base.OnSelect(eventData);
			viewHandler.CardView.EnableSelectionOuterGlow(state: true);
			viewHandler.Select();
			if (viewHandler.IsIdle)
			{
				viewHandler.CardView.Hover();
				viewHandler.CardView.EnableTilt();
				RegisterTiltBindings();
				RegisterBanishBindings();
				RegisterReRollBindings();
				_menuController.MenuView.ShowDropCardGlyph(state: true);
				_menuController.MenuView.ShowEquipAcceptGlyph(state: false);
				_menuController.MenuView.ShowEquipCancelGlyph(state: false);
				_menuController.MenuView.ShowSwapCardAcceptGlyph(state: false);
				_menuController.MenuView.ShowSwapCardCancelGlyph(state: false);
			}
			if (viewHandler.IsDropped)
			{
				if ((bool)viewHandler.CardSlot.HandSlotView)
				{
					_menuController.MenuView.HandView.UnfoldHand(viewHandler.CardSlot.HandSlotView).Forget();
				}
				RegisterDiscardBindings();
				if (_menuController.MenuView.HandView.CanMagnetLockToAnySlot(viewHandler))
				{
					_menuController.MenuView.ShowDropCardGlyph(state: false);
					_menuController.MenuView.ShowEquipAcceptGlyph(state: false);
					_menuController.MenuView.ShowEquipCancelGlyph(state: false);
					_menuController.MenuView.ShowSwapCardAcceptGlyph(state: true);
					_menuController.MenuView.ShowSwapCardCancelGlyph(state: false);
				}
			}
			TransitionToSelectedState();
		}

		public override void OnDeselect(BaseEventData eventData)
		{
			base.OnDeselect(eventData);
			viewHandler.CardView.EnableSelectionOuterGlow(state: false);
			viewHandler.UnSelect();
			if (viewHandler.IsIdle)
			{
				UnRegisterTiltBindings();
				viewHandler.CardView.UnHover();
				viewHandler.CardView.DisableTilt();
			}
			UnRegisterReRollBindings();
			UnRegisterDiscardBindings();
			UnRegisterBanishBindings();
			TransitionToUnSelectedState();
		}

		public override void OnMove(AxisEventData eventData)
		{
			if (!viewHandler.IsDragging)
			{
				base.OnMove(eventData);
			}
		}

		public override void OnSubmit(BaseEventData eventData)
		{
			if (!_menuController.IsMergingCards && !_menuController.IsDraggingCard && !_menuController.IsSwappingHandSlot && (!viewHandler.IsDropped || !(viewHandler.CardSlot != null) || !(viewHandler.CardSlot.HandSlotView != null) || !viewHandler.CardSlot.HandSlotView.IsBusy) && !viewHandler.IsDragging && !_isHoldingBanish && !_isHoldingDiscard && !_isHoldingReRoll && TryClaimLock())
			{
				if (viewHandler.IsDropped)
				{
					TransitionToSwapInHandMode().Forget();
				}
				else if (viewHandler.IsIdle)
				{
					TransitionToDropToHandMode().Forget();
				}
			}
		}

		protected virtual void Drag()
		{
			if ((bool)viewHandler && viewHandler.enabled)
			{
				Vector3 mousePosition = Input.mousePosition;
				mousePosition.z = viewHandler.Transform.position.z;
				float x = Mathf.Clamp(mousePosition.x, 0f, Screen.width);
				float y = Mathf.Clamp(mousePosition.y, 0f, Screen.height);
				viewHandler.Transform.position = Vector3.Lerp(viewHandler.Transform.position, new Vector3(x, y, mousePosition.z), 0.5f);
			}
		}

		public override void OnBeginDrag(PointerEventData eventData)
		{
			if (base.CurrentType != ControllerType.Joystick && TryClaimLock())
			{
				viewHandler.TransitionToDragging();
				viewHandler.CardView.DisableTilt();
				if (viewHandler.HasBeenDropped)
				{
					_menuController.TransitionToSwappingCard();
					UnRegisterReRollBindings();
					UnRegisterBanishBindings();
					UnRegisterDiscardBindings();
					_menuController.MenuView.EnableReRoll(state: false);
					_menuController.MenuView.EnableBanish(state: false);
					_menuController.MenuView.EnableDiscard(state: true);
					_menuController.MenuView.EnableDiscardGlyph(state: false);
				}
				else
				{
					_menuController.TransitionToDraggingCardToHand();
					UnRegisterReRollBindings();
					UnRegisterBanishBindings();
					UnRegisterDiscardBindings();
					_menuController.MenuView.EnableReRoll(state: true);
					_menuController.MenuView.EnableBanish(state: true);
					_menuController.MenuView.EnableDiscard(state: false);
					_menuController.MenuView.EnableDiscardGlyph(state: false);
				}
			}
		}

		public override void OnDrag(PointerEventData eventData)
		{
			if (!Application.isFocused)
			{
				OnEndDrag(eventData);
				return;
			}
			if (viewHandler.IsDragging)
			{
				Drag();
				if (!viewHandler.HasBeenDropped)
				{
					_menuController.MenuView.EnableReRoll(state: true);
					_menuController.MenuView.EnableBanish(state: true);
				}
			}
			if (viewHandler.HasBeenDropped)
			{
				_menuController.MenuView.EnableDiscard(state: true);
			}
			else
			{
				_menuController.MenuView.EnableDiscard(state: false);
			}
		}

		public override void OnEndDrag(PointerEventData eventData)
		{
			_menuController.TransitionToNoSelection();
			viewHandler?.TransitionToIdleOrDropped();
			_menuController.MenuView.EnableReRoll(state: false);
			_menuController.MenuView.EnableDiscard(state: false);
			_menuController.MenuView.EnableBanish(state: false);
		}

		public override void OnPointerEnter(PointerEventData eventData)
		{
			if (base.CurrentType == ControllerType.Joystick || !Application.isFocused)
			{
				return;
			}
			if (_menuController.IsFirstInteraction)
			{
				_menuController.IsFirstInteraction = false;
			}
			else if (!viewHandler.IsDragging && !eventData.dragging && !_menuController.IsSortingDeck && !_menuController.IsMergingCards && !_isHoldingReRoll && !_isHoldingBanish && !_isHoldingDiscard && !_menuController.IsHandSlotSelected && !_menuController.IsSwappingHandSlot && TryClaimLock())
			{
				base.OnPointerEnter(eventData);
				_isPointerOver = true;
				viewHandler.Select();
				if (viewHandler.IsIdle)
				{
					viewHandler.CardView.Hover();
					viewHandler.CardView.EnableTilt();
					viewHandler.CardView.EnableSelectionOuterGlow(state: true);
					viewHandler.CardView.EnableIdleAnimation(state: false);
					RegisterReRollBindings();
					RegisterBanishBindings();
					UnRegisterDiscardBindings();
				}
				if (viewHandler.IsDropped)
				{
					viewHandler.CardView.EnableSelectionOuterGlow(state: true);
					UnRegisterReRollBindings();
					UnRegisterBanishBindings();
					RegisterDiscardBindings();
				}
				TransitionToSelectedState();
			}
		}

		public override void OnPointerExit(PointerEventData eventData)
		{
			_isPointerOver = false;
			if (!_menuController.IsDraggingCard && !eventData.dragging && !viewHandler.IsDragging && !_isHoldingReRoll && !_isHoldingBanish && !_isHoldingDiscard)
			{
				base.OnPointerExit(eventData);
				viewHandler.UnSelect();
				if (viewHandler.IsIdle)
				{
					viewHandler.CardView.UnHover();
					viewHandler.CardView.EnableIdleAnimation(state: true);
					UnRegisterReRollBindings();
					UnRegisterBanishBindings();
				}
				else
				{
					UnRegisterDiscardBindings();
				}
				TransitionToUnSelectedState();
				viewHandler.CardView.EnableSelectionOuterGlow(state: false);
				viewHandler.CardView.DisableTilt();
				_menuController.MenuView.EnableDiscard(state: false);
				_menuController.MenuView.EnableDiscardGlyph(state: false);
			}
		}

		public override void OnPointerMove(PointerEventData eventData)
		{
			if (Application.isFocused)
			{
				viewHandler.CardView.ApplyTilt(Input.mousePosition);
			}
		}

		private void RegisterTiltBindings()
		{
			_menuController.OnUIRightAnalogHorizontal += ApplyHorizontalTilt;
			_menuController.OnUIRightAnalogVertical += ApplyVerticalTilt;
		}

		private void UnRegisterTiltBindings()
		{
			_menuController.OnUIRightAnalogHorizontal -= ApplyHorizontalTilt;
			_menuController.OnUIRightAnalogVertical -= ApplyVerticalTilt;
		}

		private void RegisterReRollBindings()
		{
			UnRegisterReRollBindings();
			_menuController.OnUIButton4Pressed += StartGamepadReRoll;
			_menuController.MenuView.EnableReRoll(state: true);
			_menuController.MenuView.EnableReRollGlyph(state: true);
		}

		private void UnRegisterReRollBindings()
		{
			_menuController.OnUIButton4Pressed -= StartGamepadReRoll;
			_menuController.MenuView.EnableReRoll(state: false);
			_menuController.MenuView.EnableReRollGlyph(state: false);
		}

		private void RegisterDiscardBindings()
		{
			UnRegisterDiscardBindings();
			_menuController.OnUIButton4Pressed += StartGamepadDiscard;
			_menuController.MenuView.EnableDiscard(state: true);
			_menuController.MenuView.EnableDiscardGlyph(state: true);
		}

		private void UnRegisterDiscardBindings()
		{
			_menuController.OnUIButton4Pressed -= StartGamepadDiscard;
			_menuController.MenuView.EnableDiscard(state: false);
		}

		private void RegisterBanishBindings()
		{
			UnRegisterBanishBindings();
			_menuController.OnUICancelPressed += StartGamepadBanish;
			_menuController.MenuView.EnableBanish(state: true);
			_menuController.MenuView.EnableBanishGlyph(state: true);
		}

		private void UnRegisterBanishBindings()
		{
			_menuController.OnUICancelPressed -= StartGamepadBanish;
			_menuController.MenuView.EnableBanish(state: false);
			_menuController.MenuView.EnableBanishGlyph(state: false);
		}

		private void ApplyHorizontalTilt(InputActionEventData data)
		{
			if (data.GetAxisTimeActive() == 0.0)
			{
				viewHandler.CardView.StopTilt();
			}
			else
			{
				ApplySelectTilt(new Vector2(data.GetAxisRaw(), 0f));
			}
		}

		private void ApplyVerticalTilt(InputActionEventData data)
		{
			if (data.GetAxisTimeActive() == 0.0)
			{
				viewHandler.CardView.StopTilt();
			}
			else
			{
				ApplySelectTilt(new Vector2(0f, data.GetAxisRaw()));
			}
		}

		private void ApplySelectTilt(Vector2 input)
		{
			viewHandler.CardView.ApplyTilt(input * 0.5f, isPosition: false);
		}

		private void StartGamepadReRoll()
		{
			if (!_menuController.IsSortingDeck && !viewHandler.IsDragging && !IsDroppingToHand && !viewHandler.HasBeenDropped && !_isHoldingReRoll && !_isHoldingBanish && !_isHoldingDiscard)
			{
				_isHoldingReRoll = true;
				_menuController.OnUIButton4Released += StopGamepadReRoll;
				_menuController.MenuView.StartReRoll(viewHandler);
			}
		}

		private void StopGamepadReRoll()
		{
			_isHoldingReRoll = false;
			_menuController.OnUIButton4Released -= StopGamepadReRoll;
			_menuController.MenuView.StopReRoll(viewHandler);
			if (!_isPointerOver && EventSystem.current.currentSelectedGameObject != base.gameObject)
			{
				PointerEventData eventData = new PointerEventData(EventSystem.current);
				OnPointerExit(eventData);
			}
		}

		private void StartGamepadDiscard()
		{
			if (!(ActiveHandler != this) && !_menuController.IsSortingDeck && !viewHandler.IsDragging && !IsDroppingToHand && viewHandler.HasBeenDropped && !_isHoldingReRoll && !_isHoldingBanish && !_isHoldingDiscard)
			{
				_isHoldingDiscard = true;
				_menuController.OnUIButton4Released += StopGamepadDiscard;
				_menuController.MenuView.StartDiscard(viewHandler);
			}
		}

		private void StopGamepadDiscard()
		{
			_isHoldingDiscard = false;
			_menuController.OnUIButton4Released -= StopGamepadDiscard;
			_menuController.MenuView.StopDiscard(viewHandler);
			if (!_isPointerOver && EventSystem.current.currentSelectedGameObject != base.gameObject)
			{
				PointerEventData eventData = new PointerEventData(EventSystem.current);
				OnPointerExit(eventData);
			}
		}

		private void StartGamepadBanish()
		{
			if (!(ActiveHandler != this) && !_menuController.IsSortingDeck && !viewHandler.IsDragging && !IsDroppingToHand && !viewHandler.HasBeenDropped && !_isHoldingReRoll && !_isHoldingBanish && !_isHoldingDiscard)
			{
				_isHoldingBanish = true;
				_menuController.OnUICancelReleased += StopGamepadBanish;
				_menuController.MenuView.StartBanish(viewHandler);
			}
		}

		private void StopGamepadBanish()
		{
			_isHoldingBanish = false;
			_menuController.OnUICancelReleased -= StopGamepadBanish;
			_menuController.MenuView.StopBanish(viewHandler);
			if (!_isPointerOver && EventSystem.current.currentSelectedGameObject != base.gameObject)
			{
				PointerEventData eventData = new PointerEventData(EventSystem.current);
				OnPointerExit(eventData);
			}
		}

		private async UniTask TransitionToDropToHandMode()
		{
			if (IsLocking || !TryClaimLock() || !_menuController.MenuView.HandView.CanMagnetLockToAnySlot(viewHandler, out var slotView))
			{
				return;
			}
			ResetActiveModeCts();
			EventSystem.current.SetSelectedGameObject(null);
			TransitionToDraggingState();
			viewHandler.TransitionToDragging();
			_menuController.TransitionToDraggingCardToHand();
			_selectedHandSlot = slotView;
			_menuController.MenuView.LockFocusOnHandGroup();
			IsLocking = true;
			try
			{
				if (await TryDragToHand(_activeModeCTS.Token) && !_activeModeCTS.IsCancellationRequested)
				{
					RegisterDropToHandBindings();
				}
			}
			catch (OperationCanceledException)
			{
				CancelDrop();
			}
			finally
			{
				IsLocking = false;
			}
		}

		private async void OnDropSubmit()
		{
			try
			{
				if (!IsLocking)
				{
					IsLocking = true;
					ClearBindings();
					PlayerHandSlotView selectedHandSlot = _selectedHandSlot;
					_activeModeCTS?.Cancel();
					_activeModeCTS?.Dispose();
					_activeModeCTS = null;
					TransitionToUnSelectedState();
					if ((bool)selectedHandSlot)
					{
						await selectedHandSlot.TryDropCardOnSlot(viewHandler);
					}
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
			finally
			{
				IsLocking = false;
			}
		}

		private void OnDropCancel()
		{
			if (!IsLocking)
			{
				_menuController.MenuView.ResetPermanentFocus();
				ClearBindings();
				_selectedHandSlot.TryStopMagnetLock(viewHandler);
				viewHandler.TransitionToIdleOrDropped();
				_menuController.MenuView.SetFocusOnOfferings();
				TransitionToUnSelectedState();
				EventSystem.current.SetSelectedGameObject(base.gameObject);
			}
		}

		private void CancelDrop()
		{
			_menuController.MenuView.ResetPermanentFocus();
			ClearBindings();
			_selectedHandSlot?.TryStopMagnetLock(viewHandler);
			viewHandler?.TransitionToIdleOrDropped();
			TransitionToUnSelectedState();
			EventSystem.current.SetSelectedGameObject(null);
		}

		private async UniTask<bool> TryDragToHand(CancellationToken token)
		{
			PlayerHandSlotView playerHandSlotView;
			try
			{
				playerHandSlotView = await _menuController.MenuView.HandView.TryMagnetLockFirstAvailableSlot(viewHandler).AttachExternalCancellation(token);
			}
			catch (OperationCanceledException)
			{
				return false;
			}
			if (!playerHandSlotView)
			{
				_selectedHandSlot = null;
				return false;
			}
			_selectedHandSlot = playerHandSlotView;
			return true;
		}

		private async UniTask OnDropMoveRight()
		{
			try
			{
				if (!IsLocking)
				{
					IsLocking = true;
					ResetActiveModeCts();
					ClearBindings();
					PlayerHandSlotView selectedHandSlot = _selectedHandSlot;
					PlayerHandSlotView playerHandSlotView = await _menuController.MenuView.HandView.TryMagnetLockNextAvailableSlot(selectedHandSlot, viewHandler).AttachExternalCancellation(_activeModeCTS.Token);
					if ((bool)playerHandSlotView && !_activeModeCTS.IsCancellationRequested)
					{
						_selectedHandSlot = playerHandSlotView;
					}
					RegisterDropToHandBindings();
				}
			}
			catch (OperationCanceledException)
			{
			}
			finally
			{
				IsLocking = false;
			}
		}

		private async UniTask OnDropMoveLeft()
		{
			try
			{
				if (!IsLocking)
				{
					IsLocking = true;
					ResetActiveModeCts();
					ClearBindings();
					PlayerHandSlotView selectedHandSlot = _selectedHandSlot;
					PlayerHandSlotView playerHandSlotView = await _menuController.MenuView.HandView.TryMagnetLockPreviousAvailableSlot(selectedHandSlot, viewHandler).AttachExternalCancellation(_activeModeCTS.Token);
					if ((bool)playerHandSlotView && !_activeModeCTS.IsCancellationRequested)
					{
						_selectedHandSlot = playerHandSlotView;
					}
					RegisterDropToHandBindings();
				}
			}
			catch (OperationCanceledException)
			{
			}
			finally
			{
				IsLocking = false;
			}
		}

		private void RegisterDropToHandBindings()
		{
			_menuController.OnUISubmitPressed += OnDropSubmit;
			_menuController.OnUICancelPressed += OnDropCancel;
			_menuController.OnUIDirectionalRightPressed += OnDropMoveRight;
			_menuController.OnUIDirectionalLeftPressed += OnDropMoveLeft;
			_menuController.MenuView.ShowDropCardGlyph(state: false);
			_menuController.MenuView.ShowEquipAcceptGlyph(state: true);
			_menuController.MenuView.ShowEquipCancelGlyph(state: true);
			_menuController.MenuView.ShowSwapCardAcceptGlyph(state: false);
			_menuController.MenuView.ShowSwapCardCancelGlyph(state: false);
			UnRegisterReRollBindings();
			UnRegisterBanishBindings();
		}

		private void UnRegisterDropToHandBindings()
		{
			_menuController.OnUISubmitPressed -= OnDropSubmit;
			_menuController.OnUICancelPressed -= OnDropCancel;
			_menuController.OnUIDirectionalRightPressed -= OnDropMoveRight;
			_menuController.OnUIDirectionalLeftPressed -= OnDropMoveLeft;
			_menuController.MenuView.ShowDropCardGlyph(state: false);
			_menuController.MenuView.ShowEquipAcceptGlyph(state: false);
			_menuController.MenuView.ShowEquipCancelGlyph(state: false);
			_menuController.MenuView.ShowSwapCardAcceptGlyph(state: false);
			_menuController.MenuView.ShowSwapCardCancelGlyph(state: false);
		}

		private async UniTask TransitionToSwapInHandMode()
		{
			try
			{
				if (!IsLocking && _menuController.MenuView.HandView.CanMagnetLockToAnySlot(viewHandler))
				{
					ResetActiveModeCts();
					EventSystem.current.SetSelectedGameObject(null);
					viewHandler.TransitionToDragging();
					_menuController.TransitionToSwappingCard();
					TransitionToSwappingState();
					_beforeSwapHandSlot = viewHandler.CardSlot.HandSlotView;
					_selectedHandSlot = _beforeSwapHandSlot;
					IsLocking = true;
					await _beforeSwapHandSlot.TryMagnetLock(viewHandler, ignoreAssignedSlot: false).AttachExternalCancellation(_activeModeCTS.Token);
					if (!_activeModeCTS.IsCancellationRequested)
					{
						_beforeSwapHandSlot.SetAvailableEquipmentSlotView(null);
						RegisterSwapHandBindings();
					}
				}
			}
			catch (OperationCanceledException)
			{
				CancelSwap();
			}
			finally
			{
				IsLocking = false;
			}
		}

		private async void OnSwapSubmit()
		{
			if (IsLocking)
			{
				return;
			}
			if (_beforeSwapHandSlot == _selectedHandSlot)
			{
				Debug.Log("CPM INPUT HANDLER - " + viewHandler.gameObject.name + ": Hand slot is not being swapped");
				OnSwapCancel();
				return;
			}
			try
			{
				IsLocking = true;
				ClearBindings();
				_activeModeCTS?.Cancel();
				_activeModeCTS?.Dispose();
				_activeModeCTS = null;
				if (_selectedHandSlot != null)
				{
					await _selectedHandSlot.TryDropCardOnSlot(viewHandler);
				}
				_menuController.MenuView.ResetPermanentFocus();
				UIEquipmentCardViewHandler firstFoundEquipmentView = _selectedHandSlot.GetFirstFoundEquipmentView();
				if ((bool)firstFoundEquipmentView)
				{
					EventSystem.current.SetSelectedGameObject(firstFoundEquipmentView.gameObject);
					_menuController.TransitionToCardSelected(firstFoundEquipmentView);
					return;
				}
				PlayerHandSlotView firstHandSlotWithEquipments = _menuController.MenuView.HandView.GetFirstHandSlotWithEquipments();
				if ((bool)firstHandSlotWithEquipments)
				{
					firstFoundEquipmentView = firstHandSlotWithEquipments.GetFirstFoundEquipmentView();
					_selectedHandSlot = firstHandSlotWithEquipments;
					EventSystem.current.SetSelectedGameObject(firstFoundEquipmentView.gameObject);
					_menuController.TransitionToCardSelected(firstFoundEquipmentView);
				}
				else
				{
					EventSystem.current.SetSelectedGameObject(null);
					_menuController.TransitionToNoSelection();
				}
			}
			catch (Exception)
			{
			}
			finally
			{
				IsLocking = false;
			}
		}

		private async void OnSwapCancel()
		{
			try
			{
				if (!IsLocking)
				{
					IsLocking = true;
					ClearBindings();
					TransitionToUnSelectedState();
					_menuController.MenuView.ResetPermanentFocus();
					_beforeSwapHandSlot.TryMagnetLock(viewHandler, ignoreAssignedSlot: false).Forget();
					await _beforeSwapHandSlot.TryDropCardOnSlot(viewHandler);
					_selectedHandSlot = _beforeSwapHandSlot;
					EventSystem.current.SetSelectedGameObject(base.gameObject);
					_menuController.TransitionToCardSelected(viewHandler);
				}
			}
			catch (Exception)
			{
			}
			finally
			{
				IsLocking = false;
			}
		}

		private void CancelSwap()
		{
			ClearBindings();
			TransitionToUnSelectedState();
			_menuController.MenuView.ResetPermanentFocus();
			_selectedHandSlot?.TryStopMagnetLock(viewHandler);
			if (_beforeSwapHandSlot != null)
			{
				_beforeSwapHandSlot.TryMagnetLock(viewHandler, ignoreAssignedSlot: false).Forget();
				_beforeSwapHandSlot.TryDropCardOnSlot(viewHandler).Forget();
				_selectedHandSlot = _beforeSwapHandSlot;
			}
			EventSystem.current.SetSelectedGameObject(null);
			_menuController.TransitionToNoSelection();
		}

		private async UniTask OnSwapMoveRight()
		{
			if (IsLocking)
			{
				return;
			}
			IsLocking = true;
			try
			{
				ResetActiveModeCts();
				ClearBindings();
				PlayerHandSlotView selectedHandSlot = _selectedHandSlot;
				PlayerHandSlotView playerHandSlotView = await _menuController.MenuView.HandView.TryMagnetLockNextAvailableSlot(selectedHandSlot, viewHandler, ignoreAssignedSlot: false).AttachExternalCancellation(_activeModeCTS.Token);
				if ((bool)playerHandSlotView && !_activeModeCTS.IsCancellationRequested)
				{
					_selectedHandSlot = playerHandSlotView;
				}
				RegisterSwapHandBindings();
			}
			catch (OperationCanceledException)
			{
			}
			finally
			{
				IsLocking = false;
			}
		}

		private async UniTask OnSwapMoveLeft()
		{
			if (IsLocking)
			{
				return;
			}
			IsLocking = true;
			try
			{
				ResetActiveModeCts();
				ClearBindings();
				PlayerHandSlotView selectedHandSlot = _selectedHandSlot;
				PlayerHandSlotView playerHandSlotView = await _menuController.MenuView.HandView.TryMagnetLockPreviousAvailableSlot(selectedHandSlot, viewHandler, ignoreAssignedSlot: false).AttachExternalCancellation(_activeModeCTS.Token);
				if ((bool)playerHandSlotView && !_activeModeCTS.IsCancellationRequested)
				{
					_selectedHandSlot = playerHandSlotView;
				}
				RegisterSwapHandBindings();
			}
			catch (OperationCanceledException)
			{
			}
			finally
			{
				IsLocking = false;
			}
		}

		private void RegisterSwapHandBindings()
		{
			_menuController.MenuView.Controller.OnUISubmitPressed += OnSwapSubmit;
			_menuController.MenuView.Controller.OnUICancelPressed += OnSwapCancel;
			_menuController.MenuView.Controller.OnUIDirectionalRightPressed += OnSwapMoveRight;
			_menuController.MenuView.Controller.OnUIDirectionalLeftPressed += OnSwapMoveLeft;
			_menuController.MenuView.ShowDropCardGlyph(state: false);
			_menuController.MenuView.ShowEquipAcceptGlyph(state: true);
			_menuController.MenuView.ShowEquipCancelGlyph(state: false);
			_menuController.MenuView.ShowSwapCardAcceptGlyph(state: false);
			_menuController.MenuView.ShowSwapCardCancelGlyph(state: true);
		}

		private void UnRegisterSwapHandBindings()
		{
			_menuController.MenuView.Controller.OnUISubmitPressed -= OnSwapSubmit;
			_menuController.MenuView.Controller.OnUICancelPressed -= OnSwapCancel;
			_menuController.MenuView.Controller.OnUIDirectionalRightPressed -= OnSwapMoveRight;
			_menuController.MenuView.Controller.OnUIDirectionalLeftPressed -= OnSwapMoveLeft;
			_menuController.MenuView.ShowDropCardGlyph(state: false);
			_menuController.MenuView.ShowEquipAcceptGlyph(state: false);
			_menuController.MenuView.ShowEquipCancelGlyph(state: false);
			_menuController.MenuView.ShowSwapCardAcceptGlyph(state: false);
			_menuController.MenuView.ShowSwapCardCancelGlyph(state: false);
		}
	}
}
