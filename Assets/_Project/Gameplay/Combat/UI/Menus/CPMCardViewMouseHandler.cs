using System;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.UI.Cards;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.HellMaiden.UI.Menus
{
	[Obsolete("This class is deprecated. Use CPMCardViewInputHandler instead")]
	public class CPMCardViewMouseHandler : UICardViewMouseHandler
	{
		private CardPickMenuController _menuController;

		protected override void Awake()
		{
			base.Awake();
			if (!_menuController)
			{
				_menuController = UICardPickMenuView.Instance.Controller;
			}
		}

		public void OnDisable()
		{
			PointerEventData eventData = new PointerEventData(EventSystem.current);
			if (viewHandler.IsDragging)
			{
				OnEndDrag(eventData);
			}
			else
			{
				OnPointerExit(eventData);
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
			viewHandler.TransitionToDragging();
			viewHandler.CardView.DisableTilt();
			if (viewHandler.HasBeenDropped)
			{
				_menuController.TransitionToSwappingCard();
				_menuController.MenuView.EnableReRoll(state: false);
				_menuController.MenuView.EnableReRollGlyph(state: false);
				_menuController.MenuView.EnableDiscard(state: true);
				_menuController.MenuView.EnableDiscardGlyph(state: false);
				_menuController.MenuView.EnableBanish(state: false);
				_menuController.MenuView.EnableBanishGlyph(state: false);
			}
			else
			{
				_menuController.TransitionToDraggingCardToHand();
				_menuController.MenuView.EnableReRoll(state: true);
				_menuController.MenuView.EnableReRollGlyph(state: false);
				_menuController.MenuView.EnableDiscard(state: false);
				_menuController.MenuView.EnableDiscardGlyph(state: false);
				_menuController.MenuView.EnableBanish(state: true);
				_menuController.MenuView.EnableBanishGlyph(state: false);
			}
		}

		public override void OnDrag(PointerEventData eventData)
		{
			if (viewHandler.IsDragging)
			{
				Drag();
			}
			if (viewHandler.HasBeenDropped)
			{
				_menuController.MenuView.EnableReRoll(state: false);
				_menuController.MenuView.EnableDiscard(state: true);
				_menuController.MenuView.EnableBanish(state: false);
			}
			else
			{
				_menuController.MenuView.EnableReRoll(state: true);
				_menuController.MenuView.EnableDiscard(state: false);
				_menuController.MenuView.EnableBanish(state: true);
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
			if (eventData.dragging || _menuController.IsMergingCards)
			{
				return;
			}
			if (viewHandler.IsDropped && (!_menuController.IsHandSlotSelected || !_menuController.IsSwappingHandSlot))
			{
				viewHandler.Select();
				viewHandler.CardView.EnableSelectionOuterGlow(state: true);
				_menuController.TransitionToCardSelected(viewHandler);
				_menuController.MenuView.EnableReRoll(state: false);
				_menuController.MenuView.EnableReRollGlyph(state: false);
				_menuController.MenuView.EnableDiscard(state: true);
				_menuController.MenuView.EnableDiscardGlyph(state: false);
				_menuController.MenuView.EnableBanish(state: false);
				_menuController.MenuView.EnableBanishGlyph(state: false);
				return;
			}
			viewHandler.Select();
			if (viewHandler.IsDragging)
			{
				viewHandler.CardView.DisableTilt();
			}
			else if (viewHandler.IsIdle)
			{
				viewHandler.CardView.Hover();
				viewHandler.CardView.EnableTilt();
				viewHandler.CardView.EnableSelectionOuterGlow(state: true);
				viewHandler.CardView.EnableIdleAnimation(state: false);
				_menuController.MenuView.EnableReRoll(state: true);
				_menuController.MenuView.EnableReRollGlyph(state: false);
				_menuController.MenuView.EnableDiscard(state: false);
				_menuController.MenuView.EnableDiscardGlyph(state: false);
				_menuController.MenuView.EnableBanish(state: true);
				_menuController.MenuView.EnableBanishGlyph(state: false);
				_menuController.TransitionToCardSelected(viewHandler);
			}
		}

		public override void OnPointerExit(PointerEventData eventData)
		{
			if (!eventData.dragging)
			{
				viewHandler.UnSelect();
				if (viewHandler.IsDropped)
				{
					_menuController.TransitionToNoSelection();
				}
				if (viewHandler.IsIdle)
				{
					viewHandler.CardView.UnHover();
					viewHandler.CardView.EnableIdleAnimation(state: true);
					_menuController.TransitionToNoSelection();
				}
				viewHandler.CardView.EnableSelectionOuterGlow(state: false);
				viewHandler.CardView.DisableTilt();
				_menuController.MenuView.EnableReRoll(state: true);
				_menuController.MenuView.EnableReRollGlyph(state: false);
				_menuController.MenuView.EnableDiscard(state: false);
				_menuController.MenuView.EnableDiscardGlyph(state: false);
				_menuController.MenuView.EnableBanish(state: true);
				_menuController.MenuView.EnableBanishGlyph(state: false);
			}
		}

		public override void OnPointerMove(PointerEventData eventData)
		{
			viewHandler.CardView.ApplyTilt(Input.mousePosition);
		}
	}
}
