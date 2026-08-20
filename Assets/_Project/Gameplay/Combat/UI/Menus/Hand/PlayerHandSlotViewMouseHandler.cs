using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.UI.Cards;
using AstralShift.Helpers;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.HellMaiden.UI.Menus.Hand
{
	public class PlayerHandSlotViewMouseHandler : MonoBehaviour, IDropHandler, IEventSystemHandler, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
	{
		[SerializeField]
		[HideInInspector]
		protected PlayerHandSlotView handSlotView;

		[SerializeField]
		private float smoothTime = 0.05f;

		[SerializeField]
		private float swapThreshold = 20f;

		[SerializeField]
		private float snapBackDuration = 0.2f;

		private CardPickMenuController _menuController;

		private UICardViewHandler DraggedCardViewHandler;

		private Vector3 _moveVelocity;

		private Tween _snapBackTween;

		private CardPickMenuController MenuController
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

		public bool IsMouseInViewport
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

		public void Awake()
		{
			Reset();
		}

		public void Reset()
		{
			if (!handSlotView)
			{
				handSlotView = GetComponent<PlayerHandSlotView>();
			}
		}

		public void OnDisable()
		{
			if (!MenuController.IsMergingCards)
			{
				_snapBackTween?.Kill();
				PointerEventData eventData = new PointerEventData(EventSystem.current);
				OnPointerExit(eventData);
			}
		}

		public void OnDrop(PointerEventData eventData)
		{
			if (!MenuController.IsMergingCards && !(eventData.pointerDrag == null) && eventData.pointerDrag.TryGetComponent<UICardViewHandler>(out var component))
			{
				handSlotView.TryDropCardOnSlot(component);
			}
		}

		public void OnPointerEnter(PointerEventData eventData)
		{
			if (MenuController.IsMergingCards)
			{
				return;
			}
			UICardViewHandler component;
			if (!IsMouseInViewport)
			{
				OnPointerExit(eventData);
			}
			else if (!eventData.dragging)
			{
				if (handSlotView.HandSlot.Equipments.Count > 0)
				{
					handSlotView.HandView.UnfoldHand(handSlotView).Forget();
				}
			}
			else if (eventData.pointerDrag.TryGetComponent<UICardViewHandler>(out component))
			{
				DraggedCardViewHandler = component;
				handSlotView.TryMagnetLock(component, ignoreAssignedSlot: false).Forget();
			}
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			if (!MenuController.IsMergingCards && (bool)eventData.pointerDrag)
			{
				DraggedCardViewHandler = null;
				handSlotView.SetAvailableEquipmentSlotView(null);
				if (eventData.pointerDrag.TryGetComponent<UICardViewHandler>(out var component))
				{
					handSlotView.TryStopMagnetLock(component);
				}
			}
		}

		public void OnBeginDrag(PointerEventData eventData)
		{
			if (!MenuController.IsMergingCards)
			{
				_snapBackTween?.Kill();
				handSlotView.HandView.FoldHand().Forget();
				handSlotView.ShowSelectionVFX(state: true);
				handSlotView.TransitionToDragging();
			}
		}

		public void OnDrag(PointerEventData eventData)
		{
			if (!MenuController.IsMergingCards)
			{
				Vector3 mousePosition = Input.mousePosition;
				mousePosition.z = handSlotView.SlotViewsContainer.position.z;
				mousePosition.y = handSlotView.SlotViewsContainer.position.y;
				handSlotView.SlotViewsContainer.position = Vector3.SmoothDamp(handSlotView.SlotViewsContainer.position, mousePosition, ref _moveVelocity, smoothTime, float.PositiveInfinity, Time.unscaledDeltaTime);
				EvaluatePositionAndSwap(handSlotView.transform.GetSiblingIndex(), handSlotView.SlotViewsContainer.position);
			}
		}

		public bool EvaluatePositionAndSwap(int index, Vector3 currentPosition)
		{
			float resAdjustedScreenSpaceOffset = UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(swapThreshold);
			PlayerHandSlotView nextHandSlotView = handSlotView.HandView.GetNextHandSlotView(handSlotView);
			if (nextHandSlotView != null && currentPosition.x > nextHandSlotView.transform.position.x + resAdjustedScreenSpaceOffset)
			{
				handSlotView.HandView.SwapSlotAfter(index);
				return true;
			}
			PlayerHandSlotView previousHandSlotView = handSlotView.HandView.GetPreviousHandSlotView(handSlotView);
			if ((bool)previousHandSlotView && currentPosition.x < previousHandSlotView.transform.position.x - resAdjustedScreenSpaceOffset)
			{
				handSlotView.HandView.SwapSlotBefore(index);
				return true;
			}
			return false;
		}

		public void OnEndDrag(PointerEventData eventData)
		{
			if (!MenuController.IsMergingCards)
			{
				handSlotView.ShowSelectionVFX(state: false);
				handSlotView.StopSelectionMovement();
				handSlotView.TransitionToIdle();
				_snapBackTween?.Kill();
				_snapBackTween = handSlotView.SlotViewsContainer.DOLocalMove(Vector3.zero, snapBackDuration).SetEase(Ease.OutCubic).SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			}
		}
	}
}
