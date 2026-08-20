using System.Threading;
using AstralShift.HellMaiden.Controllers;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Menus.Hand
{
	public class PlayerHandSlotViewGamepadHandler : Selectable, ISubmitHandler, IEventSystemHandler
	{
		[SerializeField]
		[HideInInspector]
		protected PlayerHandSlotView handSlotView;

		private CardPickMenuController _menuController;

		private CancellationTokenSource _disableCts;

		public CardPickMenuController MenuController
		{
			get
			{
				if (_menuController == null)
				{
					_menuController = UICardPickMenuView.Instance.Controller;
				}
				return _menuController;
			}
		}

		private CancellationToken DisableToken => _disableCts?.Token ?? base.destroyCancellationToken;

		protected override void Awake()
		{
			base.Awake();
			if (Application.isPlaying && !handSlotView)
			{
				handSlotView = GetComponent<PlayerHandSlotView>();
			}
		}

		protected override void OnEnable()
		{
			base.OnEnable();
			if (Application.isPlaying)
			{
				_disableCts = new CancellationTokenSource();
			}
		}

		protected override void OnDisable()
		{
			base.OnDisable();
			if (Application.isPlaying && !MenuController.IsMergingCards)
			{
				_disableCts?.Cancel();
				_disableCts?.Dispose();
				_disableCts = null;
				ClearBindings();
			}
		}

		public override void OnSelect(BaseEventData eventData)
		{
			if (!MenuController.IsMergingCards)
			{
				handSlotView.TransitionToIdle();
				ResetPosition();
				UICardPickMenuView.Instance.ShowSlotSwapAcceptGlyph(state: true);
				handSlotView.ShowSelectionVFX(state: true, enableArrow: true);
				handSlotView.StartSelectionArrowMovement();
			}
		}

		public override void OnDeselect(BaseEventData eventData)
		{
			if (!MenuController.IsMergingCards)
			{
				ResetPosition();
				if (handSlotView.IsDragging)
				{
					CancelSwap();
					handSlotView.TransitionToIdle();
				}
				else
				{
					handSlotView.ShowSelectionVFX(state: false, enableArrow: true);
					handSlotView.StopSelectionArrowMovement();
				}
			}
		}

		public override void OnPointerDown(PointerEventData eventData)
		{
		}

		public override void OnPointerUp(PointerEventData eventData)
		{
		}

		public override void OnPointerEnter(PointerEventData eventData)
		{
		}

		public override void OnPointerExit(PointerEventData eventData)
		{
		}

		public void OnSubmit(BaseEventData eventData)
		{
			if (!MenuController.IsMergingCards && handSlotView.IsIdle)
			{
				StartSwap();
			}
		}

		private void StartSwap()
		{
			EventSystem.current.SetSelectedGameObject(null);
			handSlotView.TransitionToDragging();
			MoveUp();
			UICardPickMenuView.Instance.Controller.OnUISubmitPressed += StopSwap;
			UICardPickMenuView.Instance.Controller.OnUIDirectionalRightPressed += OnSwapMoveRight;
			UICardPickMenuView.Instance.Controller.OnUIDirectionalLeftPressed += OnSwapMoveLeft;
			handSlotView.ShowSelectionVFX(state: true, enableArrow: true);
			handSlotView.StopSelectionArrowMovement();
			UICardPickMenuView.Instance.ShowSlotSwapAcceptGlyph(state: true);
		}

		private async void StopSwap()
		{
			ClearBindings();
			ResetPosition();
			await UniTask.NextFrame(DisableToken);
			if (!DisableToken.IsCancellationRequested)
			{
				EventSystem.current.SetSelectedGameObject(handSlotView.gameObject);
			}
		}

		private void CancelSwap()
		{
			handSlotView.ShowSelectionVFX(state: false, enableArrow: true);
			handSlotView.StopSelectionArrowMovement();
			UICardPickMenuView.Instance.Controller.OnUISubmitPressed -= StopSwap;
			UICardPickMenuView.Instance.Controller.OnUIDirectionalRightPressed -= OnSwapMoveRight;
			UICardPickMenuView.Instance.Controller.OnUIDirectionalLeftPressed -= OnSwapMoveLeft;
			ResetPosition();
		}

		private async UniTask OnSwapMoveLeft()
		{
			if ((bool)handSlotView.HandView.GetPreviousHandSlotView(handSlotView))
			{
				handSlotView.HandView.SwapSlotBefore(base.transform.GetSiblingIndex());
				MoveUp();
			}
		}

		private async UniTask OnSwapMoveRight()
		{
			if ((bool)handSlotView.HandView.GetNextHandSlotView(handSlotView))
			{
				handSlotView.HandView.SwapSlotAfter(base.transform.GetSiblingIndex());
				MoveUp();
			}
		}

		private void MoveUp()
		{
			handSlotView.StartSelectionMovement();
		}

		public void ResetPosition()
		{
			handSlotView.StopSelectionMovement();
		}

		private void ClearBindings()
		{
			if ((bool)UICardPickMenuView.Instance)
			{
				UICardPickMenuView.Instance.Controller.OnUISubmitPressed -= StopSwap;
				UICardPickMenuView.Instance.Controller.OnUIDirectionalRightPressed -= OnSwapMoveRight;
				UICardPickMenuView.Instance.Controller.OnUIDirectionalLeftPressed -= OnSwapMoveLeft;
			}
		}

		public void ClearNavigation()
		{
			Navigation navigation = base.navigation;
			navigation.mode = Navigation.Mode.Explicit;
			navigation.wrapAround = false;
			navigation.selectOnLeft = null;
			navigation.selectOnRight = null;
			navigation.selectOnUp = null;
			navigation.selectOnDown = null;
			base.navigation = navigation;
		}

		public void SetLeftNavigation(Selectable left)
		{
			Navigation navigation = base.navigation;
			navigation.mode = Navigation.Mode.Explicit;
			navigation.wrapAround = false;
			navigation.selectOnLeft = left;
			base.navigation = navigation;
		}

		public void SetRightNavigation(Selectable right)
		{
			Navigation navigation = base.navigation;
			navigation.mode = Navigation.Mode.Explicit;
			navigation.wrapAround = false;
			navigation.selectOnRight = right;
			base.navigation = navigation;
		}
	}
}
