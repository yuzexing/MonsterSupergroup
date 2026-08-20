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
	[Obsolete("This class is deprecated. Use CPMCardViewInputHandler instead")]
	public class CPMCardViewGamepadHandler : UICardViewGamepadHandler
	{
		private CardPickMenuController _menuController;

		protected PlayerHandSlotView _beforeSwapHandSlot;

		protected PlayerHandSlotView _selectedHandSlot;

		private StateMachine _stateMachine;

		private State _selectedState;

		private State _unSelectedState;

		private State _droppingToHandState;

		private State _swappingInHandState;

		private CancellationTokenSource _disableCTS;

		private CancellationTokenSource _activeModeCTS;

		private bool _holdingDiscard;

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

		protected override void Awake()
		{
			base.Awake();
			if (!_menuController)
			{
				_menuController = UICardPickMenuView.Instance.Controller;
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

		protected override void OnEnable()
		{
			base.OnEnable();
			if (_disableCTS == null)
			{
				_disableCTS = new CancellationTokenSource();
			}
		}

		protected override void OnDisable()
		{
			base.OnDisable();
			_disableCTS?.Cancel();
			_disableCTS?.Dispose();
			_activeModeCTS?.Cancel();
			_activeModeCTS?.Dispose();
			if (!_menuController || !_menuController.IsMergingCards)
			{
				if (IsDroppingToHand)
				{
					CancelDrop();
				}
				if (IsSwappingInHand)
				{
					CancelSwap();
				}
				UnRegisterReRollBindings();
				UnRegisterDiscardBindings();
				UnRegisterBanishBindings();
				StopDiscard();
				StopBanish();
				StopReRoll();
			}
		}

		public void CancelActions()
		{
			base.OnDisable();
			_disableCTS?.Cancel();
			_disableCTS?.Dispose();
			_activeModeCTS?.Cancel();
			_activeModeCTS?.Dispose();
			if (!_menuController || !_menuController.IsMergingCards)
			{
				if (IsDroppingToHand)
				{
					CancelDrop();
				}
				if (IsSwappingInHand)
				{
					CancelSwap();
				}
				EventSystem.current.SetSelectedGameObject(null);
			}
		}

		private void ResetActiveModeCts()
		{
			_activeModeCTS?.Cancel();
			_activeModeCTS?.Dispose();
			_activeModeCTS = ((_disableCTS != null) ? CancellationTokenSource.CreateLinkedTokenSource(_disableCTS.Token) : new CancellationTokenSource());
		}

		private void TransitionToSelectedState()
		{
			_stateMachine.MakeTransition(_selectedState);
		}

		private void TransitionToUnSelectedState()
		{
			_stateMachine.MakeTransition(_unSelectedState);
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
				_menuController.MenuView.HandView.UnfoldHand(viewHandler.CardSlot.HandSlotView).AttachExternalCancellation(_disableCTS.Token).Forget();
				RegisterDiscardBindings();
				if (_menuController.MenuView.HandView.CanMagnetLockToAnySlot(viewHandler))
				{
					_menuController.MenuView.ShowDropCardGlyph(state: false);
					_menuController.MenuView.ShowEquipAcceptGlyph(state: false);
					_menuController.MenuView.ShowEquipCancelGlyph(state: false);
					_menuController.MenuView.ShowSwapCardAcceptGlyph(state: true);
					_menuController.MenuView.ShowSwapCardCancelGlyph(state: false);
					return;
				}
			}
			TransitionToSelectedState();
			_menuController.TransitionToCardSelected(null);
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
			_menuController.TransitionToNoSelection();
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
			Debug.Log("Is ViewHandler Available: " + (viewHandler != null));
			if (!viewHandler.IsDragging)
			{
				if (viewHandler.IsDropped)
				{
					TransitionToSwapInHandMode();
				}
				else if (viewHandler.IsIdle)
				{
					TransitionToDropToHandMode().AttachExternalCancellation(_disableCTS.Token).Forget();
				}
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
			_menuController.OnUIButton4Pressed += StartReRoll;
			_menuController.OnUIButton4Released += StopReRoll;
			_menuController.MenuView.EnableReRoll(state: true);
			_menuController.MenuView.EnableReRollGlyph(state: true);
		}

		private void UnRegisterReRollBindings()
		{
			_menuController.OnUIButton4Pressed -= StartReRoll;
			_menuController.OnUIButton4Released -= StopReRoll;
			_menuController.MenuView.EnableReRoll(state: false);
			_menuController.MenuView.EnableReRollGlyph(state: false);
		}

		private void RegisterDiscardBindings()
		{
			UnRegisterDiscardBindings();
			_menuController.OnUIButton4Pressed += StartDiscard;
			_menuController.OnUIButton4Released += StopDiscard;
			_menuController.MenuView.EnableDiscard(state: true);
			_menuController.MenuView.EnableDiscardGlyph(state: true);
		}

		private void UnRegisterDiscardBindings()
		{
			_menuController.OnUIButton4Pressed -= StartDiscard;
			_menuController.OnUIButton4Released -= StopDiscard;
			_menuController.MenuView.EnableDiscard(state: false);
		}

		private void RegisterBanishBindings()
		{
			UnRegisterBanishBindings();
			_menuController.OnUICancelPressed += StartBanish;
			_menuController.OnUICancelReleased += StopBanish;
			_menuController.MenuView.EnableBanish(state: true);
			_menuController.MenuView.EnableBanishGlyph(state: true);
		}

		private void UnRegisterBanishBindings()
		{
			_menuController.OnUICancelPressed -= StartBanish;
			_menuController.OnUICancelReleased -= StopBanish;
			_menuController.MenuView.EnableBanish(state: false);
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

		private void StartReRoll()
		{
			_menuController.MenuView.StartReRoll(viewHandler);
		}

		private void StopReRoll()
		{
			_menuController.MenuView.StopReRoll(viewHandler);
		}

		private void StartDiscard()
		{
			_menuController.MenuView.StartDiscard(viewHandler);
		}

		private void StopDiscard()
		{
			_menuController.MenuView.StopDiscard(viewHandler);
		}

		private void StartBanish()
		{
			_menuController.MenuView.StartBanish(viewHandler);
		}

		private void StopBanish()
		{
			_menuController.MenuView.StopBanish(viewHandler);
		}

		private async UniTask TransitionToDropToHandMode()
		{
			if (!_menuController.MenuView.HandView.CanMagnetLockToAnySlot(viewHandler, out var slotView))
			{
				return;
			}
			ResetActiveModeCts();
			EventSystem.current.SetSelectedGameObject(null);
			TransitionToDraggingState();
			viewHandler.TransitionToDragging();
			_menuController.TransitionToDraggingCardToHand();
			_selectedHandSlot = slotView;
			_menuController.MenuView.SetFocusOnHand();
			try
			{
				if (await TryDragToHand(_activeModeCTS.Token) && !_activeModeCTS.IsCancellationRequested)
				{
					RegisterDropToHandBindings();
				}
			}
			catch (OperationCanceledException)
			{
				if (_disableCTS != null && !_disableCTS.IsCancellationRequested)
				{
					CancelDrop();
				}
			}
		}

		private async void OnDropSubmit()
		{
			ClearBindings();
			PlayerHandSlotView selectedHandSlot = _selectedHandSlot;
			_activeModeCTS?.Cancel();
			_activeModeCTS?.Dispose();
			_activeModeCTS = null;
			TransitionToUnSelectedState();
			if (selectedHandSlot == null || _disableCTS == null || _disableCTS.IsCancellationRequested)
			{
				return;
			}
			try
			{
				await selectedHandSlot.TryDropCardOnSlot(viewHandler);
			}
			catch (InvalidOperationException)
			{
			}
		}

		private void OnDropCancel()
		{
			ClearBindings();
			_selectedHandSlot.TryStopMagnetLock(viewHandler);
			viewHandler.TransitionToIdleOrDropped();
			_menuController.TransitionToNoSelection();
			_menuController.MenuView.SetFocusOnOfferings();
			TransitionToUnSelectedState();
			EventSystem.current.SetSelectedGameObject(base.gameObject);
		}

		private void CancelDrop()
		{
			ClearBindings();
			_selectedHandSlot?.TryStopMagnetLock(viewHandler);
			viewHandler?.TransitionToIdleOrDropped();
			_menuController?.TransitionToNoSelection();
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
			catch (OperationCanceledException)
			{
			}
		}

		private async UniTask OnDropMoveLeft()
		{
			try
			{
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
			catch (OperationCanceledException)
			{
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

		private void TransitionToSwapInHandMode()
		{
			try
			{
				if (_menuController.MenuView.HandView.CanMagnetLockToAnySlot(viewHandler))
				{
					ResetActiveModeCts();
					EventSystem.current.SetSelectedGameObject(null);
					viewHandler.TransitionToDragging();
					_menuController.TransitionToSwappingCard();
					TransitionToSwappingState();
					_beforeSwapHandSlot = viewHandler.CardSlot.HandSlotView;
					_beforeSwapHandSlot.TryMagnetLock(viewHandler, ignoreAssignedSlot: false).Forget();
					_beforeSwapHandSlot.SetAvailableEquipmentSlotView(null);
					_selectedHandSlot = _beforeSwapHandSlot;
					RegisterSwapInHandBindings();
				}
			}
			catch (OperationCanceledException)
			{
				CancelSwap();
			}
		}

		private async void OnSwapSubmit()
		{
			ClearBindings();
			if (_beforeSwapHandSlot == _selectedHandSlot)
			{
				OnSwapCancel();
				return;
			}
			_activeModeCTS?.Cancel();
			_activeModeCTS?.Dispose();
			_activeModeCTS = null;
			if (_selectedHandSlot != null && _disableCTS != null)
			{
				await _selectedHandSlot.TryDropCardOnSlot(viewHandler);
			}
			if (_disableCTS == null || _disableCTS.IsCancellationRequested)
			{
				return;
			}
			UIEquipmentCardViewHandler foundEquipment = _selectedHandSlot.GetFirstFoundEquipmentView();
			if ((bool)foundEquipment)
			{
				EventSystem.current.SetSelectedGameObject(null);
				await UniTask.NextFrame(_disableCTS.Token);
				EventSystem.current.SetSelectedGameObject(foundEquipment.gameObject);
				_menuController.TransitionToCardSelected(null);
				return;
			}
			PlayerHandSlotView firstHandSlotWithEquipments = _menuController.MenuView.HandView.GetFirstHandSlotWithEquipments();
			if ((bool)firstHandSlotWithEquipments)
			{
				foundEquipment = firstHandSlotWithEquipments.GetFirstFoundEquipmentView();
				_selectedHandSlot = firstHandSlotWithEquipments;
				EventSystem.current.SetSelectedGameObject(null);
				await UniTask.NextFrame(_disableCTS.Token);
				EventSystem.current.SetSelectedGameObject(foundEquipment.gameObject);
				_menuController.TransitionToCardSelected(null);
			}
			else
			{
				EventSystem.current.SetSelectedGameObject(null);
			}
		}

		private async void OnSwapCancel()
		{
			ClearBindings();
			TransitionToUnSelectedState();
			if (_disableCTS == null || _disableCTS.IsCancellationRequested)
			{
				return;
			}
			_beforeSwapHandSlot.TryMagnetLock(viewHandler, ignoreAssignedSlot: false).AttachExternalCancellation(_disableCTS.Token).Forget();
			await _beforeSwapHandSlot.TryDropCardOnSlot(viewHandler).AttachExternalCancellation(_disableCTS.Token);
			if (_disableCTS != null && !_disableCTS.IsCancellationRequested)
			{
				_selectedHandSlot = _beforeSwapHandSlot;
				await UniTask.NextFrame(_disableCTS.Token);
				if (_disableCTS != null && !_disableCTS.IsCancellationRequested)
				{
					EventSystem.current.SetSelectedGameObject(base.gameObject);
					_menuController.TransitionToCardSelected(null);
				}
			}
		}

		private void CancelSwap()
		{
			ClearBindings();
			TransitionToUnSelectedState();
			_selectedHandSlot?.TryStopMagnetLock(viewHandler);
			if (_beforeSwapHandSlot != null)
			{
				_beforeSwapHandSlot.TryMagnetLock(viewHandler, ignoreAssignedSlot: false).Forget();
				_beforeSwapHandSlot.TryDropCardOnSlot(viewHandler).Forget();
				_selectedHandSlot = _beforeSwapHandSlot;
			}
			EventSystem.current.SetSelectedGameObject(null);
		}

		private async UniTask OnSwapMoveRight()
		{
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
				RegisterSwapInHandBindings();
			}
			catch (OperationCanceledException)
			{
			}
		}

		private async UniTask OnSwapMoveLeft()
		{
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
				RegisterSwapInHandBindings();
			}
			catch (OperationCanceledException)
			{
			}
		}

		private void RegisterSwapInHandBindings()
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

		private void UnRegisterSwapInHandBindings()
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

		public override void ClearBindings()
		{
			UnRegisterBanishBindings();
			UnRegisterDiscardBindings();
			UnRegisterDropToHandBindings();
			UnRegisterSwapInHandBindings();
			UnRegisterTiltBindings();
		}

		protected override void OnDestroy()
		{
			ClearBindings();
			base.OnDestroy();
		}
	}
}
