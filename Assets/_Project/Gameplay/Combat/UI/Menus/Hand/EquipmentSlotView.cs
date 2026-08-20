using AstralShift.HellMaiden.UI.Cards;
using AstralShift.Helpers.Attributes;
using Coffee.UISoftMask;
using Coffee.UISoftMaskInternal;
using DG.Tweening;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Menus.Hand
{
	public class EquipmentSlotView : CardSlotView
	{
		[Header("Pivots")]
		[SerializeField]
		protected RectTransform mergePivot;

		[SerializeField]
		protected RectTransform mergeBubblePivot;

		[Header("Mask Settings")]
		[SerializeField]
		protected SoftMask slotVisualMask;

		[SerializeField]
		protected float slotVisualMaskFadeTime = 0.1f;

		[SerializeField]
		protected float slotVisualMaskFoldFadeDelay = 0.45f;

		[SerializeField]
		protected MinMax01 slotVisualMaskFoldVisibilityRange;

		[SerializeField]
		protected MinMax01 slotVisualMaskUnfoldVisibilityRange;

		private Sequence _foldMaskSequence;

		[Space]
		[SerializeField]
		[ReadOnly]
		private UIEquipmentCardViewHandler _cardViewHandler;

		public RectTransform MergePivot => mergePivot;

		public RectTransform MergeBubblePivot => mergeBubblePivot;

		public new UIEquipmentCardViewHandler CardViewHandler => _cardViewHandler;

		protected override void Awake()
		{
			base.Awake();
			ApplyFoldSlotMask(instant: true);
		}

		public override void AssignHandSlot(PlayerHandSlotView handSlotView, int index)
		{
			base.AssignHandSlot(handSlotView, index);
			slotViewFollower.AssignParent(handSlotView.EquipmentSlotViewContainer.transform);
		}

		public override void AssignCard(UICardViewHandler cardViewHandler)
		{
			base.AssignCard(cardViewHandler);
			_cardViewHandler = cardViewHandler as UIEquipmentCardViewHandler;
			ReturnAssignedCard();
		}

		public override void ClearCardSlot()
		{
			base.ClearCardSlot();
			_cardViewHandler = null;
		}

		public virtual void ApplyFoldSlotMask(bool instant = false)
		{
			float currentMaskRangeMin = slotVisualMask.softnessRange.min;
			float currentMaskRangeMax = slotVisualMask.softnessRange.max;
			float min = slotVisualMaskFoldVisibilityRange.min;
			float max = slotVisualMaskFoldVisibilityRange.max;
			float duration = (instant ? 0f : slotVisualMaskFadeTime);
			float delay = (instant ? 0f : slotVisualMaskFoldFadeDelay);
			_foldMaskSequence?.Kill();
			_foldMaskSequence = DOTween.Sequence(this);
			_foldMaskSequence.Append(DOTween.To(() => currentMaskRangeMin, delegate(float value)
			{
				MinMax01 softnessRange = slotVisualMask.softnessRange;
				softnessRange.min = value;
				slotVisualMask.softnessRange = softnessRange;
			}, min, duration));
			_foldMaskSequence.Join(DOTween.To(() => currentMaskRangeMax, delegate(float value)
			{
				MinMax01 softnessRange = slotVisualMask.softnessRange;
				softnessRange.max = value;
				slotVisualMask.softnessRange = softnessRange;
			}, max, duration));
			_foldMaskSequence.SetDelay(delay).SetUpdate(UpdateType.Late, isIndependentUpdate: true);
		}

		public virtual void RemoveFoldSlotMask(bool instant = false)
		{
			float currentMaskRangeMin = slotVisualMask.softnessRange.min;
			float currentMaskRangeMax = slotVisualMask.softnessRange.max;
			float min = slotVisualMaskUnfoldVisibilityRange.min;
			float max = slotVisualMaskUnfoldVisibilityRange.max;
			float duration = (instant ? 0f : slotVisualMaskFadeTime);
			_foldMaskSequence?.Kill();
			_foldMaskSequence = DOTween.Sequence(this);
			_foldMaskSequence.Append(DOTween.To(() => currentMaskRangeMin, delegate(float value)
			{
				MinMax01 softnessRange = slotVisualMask.softnessRange;
				softnessRange.min = value;
				slotVisualMask.softnessRange = softnessRange;
			}, min, duration));
			_foldMaskSequence.Join(DOTween.To(() => currentMaskRangeMax, delegate(float value)
			{
				MinMax01 softnessRange = slotVisualMask.softnessRange;
				softnessRange.max = value;
				slotVisualMask.softnessRange = softnessRange;
			}, max, duration));
			_foldMaskSequence.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
		}
	}
}
