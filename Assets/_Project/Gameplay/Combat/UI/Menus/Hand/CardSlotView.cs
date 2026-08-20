using AstralShift.HellMaiden.UI.Cards;
using AstralShift.Helpers;
using DG.Tweening;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Menus.Hand
{
	public abstract class CardSlotView : MonoBehaviour
	{
		private PlayerHandSlotView _handSlotView;

		[Header("References")]
		[SerializeField]
		protected ViewFollower slotViewFollower;

		[SerializeField]
		protected RectTransform _slotContainer;

		[SerializeField]
		protected RectTransform _magnetContainer;

		[SerializeField]
		protected CanvasGroup _canvasGroup;

		protected int _index;

		private RectTransform _rectTransform;

		private Vector3 _magnetDefaultPosition;

		private Tween _magnetContainerTween;

		public PlayerHandSlotView HandSlotView => _handSlotView;

		public RectTransform SlotContainer => _slotContainer;

		public RectTransform MagnetContainer => _magnetContainer;

		public UICardViewHandler CardViewHandler { get; private set; }

		public int Index => _index;

		public RectTransform RectTransform
		{
			get
			{
				if (_rectTransform == null)
				{
					_rectTransform = GetComponent<RectTransform>();
				}
				return _rectTransform;
			}
		}

		public bool IsEmpty => CardViewHandler == null;

		protected virtual void Awake()
		{
			_magnetDefaultPosition = _magnetContainer.localPosition;
		}

		public virtual void AssignHandSlot(PlayerHandSlotView handSlotView, int index)
		{
			_handSlotView = handSlotView;
			_index = index;
		}

		public virtual void AssignCard(UICardViewHandler cardViewHandler)
		{
			CardViewHandler = cardViewHandler;
			CardViewHandler.transform.localEulerAngles = Vector3.zero;
			CardViewHandler.CardView.SetSiblingIndex(_index);
		}

		public virtual void ReorderSlot(int index)
		{
			_index = index;
		}

		public virtual void ReOrderCardView(int index)
		{
			CardViewHandler?.CardView?.SetSiblingIndex(index);
		}

		public virtual void ClearCardSlot()
		{
			CardViewHandler = null;
		}

		public void ValidateSlotOccupancy()
		{
			if (SlotContainer.childCount == 0)
			{
				ClearCardSlot();
			}
		}

		public virtual void ReturnAssignedCard()
		{
			CardViewHandler.transform.SetParent(SlotContainer.transform);
			CardViewHandler.transform.localScale = Vector3.one;
			CardViewHandler.transform.localEulerAngles = Vector3.zero;
			CardViewHandler.transform.localPosition = _slotContainer.localPosition;
			CardViewHandler.CardView.ReturnToParent();
			HandSlotView.SortEquipmentSlots();
		}

		public void ActivateSlotImage()
		{
			slotViewFollower.gameObject.SetActive(value: true);
		}

		public virtual void SetInteractable(bool interactable)
		{
			_canvasGroup.blocksRaycasts = interactable;
		}

		public void StartMagnetContainerAnimation()
		{
			Vector3 magnetDefaultPosition = _magnetDefaultPosition;
			Vector3 endValue = magnetDefaultPosition + Vector3.up * UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(5f);
			_magnetContainerTween?.Kill();
			_magnetContainer.localPosition = magnetDefaultPosition;
			_magnetContainerTween = _magnetContainer.DOLocalMove(endValue, 5f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo)
				.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
		}

		public void StopMagnetContainerAnimation()
		{
			_magnetContainerTween?.Kill();
		}
	}
}
