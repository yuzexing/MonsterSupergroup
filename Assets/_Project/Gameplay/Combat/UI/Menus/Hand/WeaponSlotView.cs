using AstralShift.HellMaiden.UI.Cards;
using AstralShift.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Menus.Hand
{
	public class WeaponSlotView : CardSlotView
	{
		[Space]
		[SerializeField]
		[ReadOnly]
		protected UIWeaponCardViewHandler _cardViewHandler;

		public new UIWeaponCardViewHandler CardViewHandler => _cardViewHandler;

		public override void AssignHandSlot(PlayerHandSlotView handSlotView, int index)
		{
			base.AssignHandSlot(handSlotView, index);
			slotViewFollower.AssignParent(handSlotView.EquipmentSlotViewContainer.transform);
		}

		public override void AssignCard(UICardViewHandler cardViewHandler)
		{
			base.AssignCard(cardViewHandler);
			_cardViewHandler = cardViewHandler as UIWeaponCardViewHandler;
			ReturnAssignedCard();
		}
	}
}
