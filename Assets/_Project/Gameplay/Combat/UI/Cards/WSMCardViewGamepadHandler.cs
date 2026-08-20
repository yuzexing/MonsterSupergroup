using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.UI.Menus;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.HellMaiden.UI.Cards
{
	public class WSMCardViewGamepadHandler : UICardViewGamepadHandler
	{
		protected WeaponSelectionMenuController _menuController;

		protected override void Awake()
		{
			base.Awake();
			if (Application.isPlaying && !_menuController)
			{
				_menuController = WeaponSelectionMenuView.Instance.Controller;
			}
		}

		public override void OnSelect(BaseEventData eventData)
		{
			base.OnSelect(eventData);
			RegisterTiltBindings();
		}

		public override void OnDeselect(BaseEventData eventData)
		{
			base.OnDeselect(eventData);
			UnRegisterTiltBindings();
		}

		public override void OnMove(AxisEventData eventData)
		{
		}

		public override void OnSubmit(BaseEventData eventData)
		{
			_menuController.MenuView.ChooseWeapon(viewHandler as UIWeaponCardViewHandler);
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

		public override void ClearBindings()
		{
			UnRegisterTiltBindings();
		}

		protected override void OnDestroy()
		{
			ClearBindings();
			base.OnDestroy();
		}
	}
}
