using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.UI.Menus;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.HellMaiden.UI.Cards
{
	public class WSMCardViewMouseHandler : UICardViewMouseHandler, IPointerClickHandler, IEventSystemHandler
	{
		private WeaponSelectionMenuController _menuController;

		protected override void Awake()
		{
			base.Awake();
			if (!_menuController)
			{
				_menuController = WeaponSelectionMenuView.Instance.Controller;
			}
		}

		public void OnDisable()
		{
		}

		protected virtual void Drag()
		{
		}

		public override void OnBeginDrag(PointerEventData eventData)
		{
		}

		public override void OnDrag(PointerEventData eventData)
		{
		}

		public override void OnEndDrag(PointerEventData eventData)
		{
		}

		public override void OnPointerEnter(PointerEventData eventData)
		{
			viewHandler.Select();
		}

		public override void OnPointerExit(PointerEventData eventData)
		{
			viewHandler.UnSelect();
			viewHandler.CardView.StopTilt();
		}

		public override void OnPointerMove(PointerEventData eventData)
		{
			viewHandler.CardView.ApplyTilt(Input.mousePosition);
		}

		public void OnPointerClick(PointerEventData eventData)
		{
			_menuController.MenuView.ChooseWeapon(viewHandler as UIWeaponCardViewHandler);
		}
	}
}
