using AstralShift.HellMaiden.UI.Cards;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Menus
{
	public class WSMCardViewContainer : UICardViewContainer
	{
		[SerializeField]
		protected CanvasGroup canvasGroup;

		public virtual void AddSlot(WSMCardSlotViewHandler slotViewHandler)
		{
			slotViewHandler.SlotView.AssignParent(base.Transform);
			slotViewHandler.SlotView.SetSiblingIndex(slotViewHandler.SlotView.SiblingIndex);
		}

		public void Show()
		{
			if ((bool)canvasGroup)
			{
				canvasGroup.alpha = 1f;
			}
		}

		public void Hide()
		{
			if ((bool)canvasGroup)
			{
				canvasGroup.alpha = 0f;
			}
		}
	}
}
