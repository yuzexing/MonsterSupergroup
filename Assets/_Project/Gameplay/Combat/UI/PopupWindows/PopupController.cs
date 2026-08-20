using System;
using AstralShift.Control;
using AstralShift.Control.Controllers;
using AstralShift.HellMaiden.UI;
using AstralShift.Managers;
using Cysharp.Threading.Tasks;
using Rewired;
using UnityEngine.EventSystems;

namespace AstralShift.UI.PopupWindows
{
	public class PopupController : GameMenuController
	{
		private PopupWindow _window;

		private bool _isClosing;

		public event Action onPopupLaunch;

		public event Action onDeactivate;

		public event Action OnUISubmitPressed;

		public event Action OnUICancelPressed;

		public event Action OnUIDirectionLeftPressed;

		public event Action OnUIDirectionRightPressed;

		public void KeepHUDOpen()
		{
			if ((bool)CombatUIManager.Instance)
			{
				CombatUIManager.Instance.OpenHUD();
			}
		}

		public async UniTask LaunchPopup(PopupWindow popupWindow, PopupContext popupContext)
		{
			_window = popupWindow;
			await _window.Open(popupContext, this);
			this.onPopupLaunch?.Invoke();
			this.onPopupLaunch = null;
		}

		public override void UIDirectionalLeft(InputActionEventData data)
		{
			if (data.eventType == InputActionEventType.NegativeButtonJustPressed)
			{
				this.OnUIDirectionLeftPressed?.Invoke();
			}
		}

		public override void UIDirectionalRight(InputActionEventData data)
		{
			if (data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnUIDirectionRightPressed?.Invoke();
			}
		}

		public override void UISubmit(InputActionEventData data)
		{
			if (data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnUISubmitPressed?.Invoke();
			}
		}

		public override void UICancelPressed(InputActionEventData data)
		{
			base.UICancelPressed(data);
			if (data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnUICancelPressed?.Invoke();
			}
		}

		protected override void OnControllerTypeChanged()
		{
			if ((bool)_window && _window.firstButton != null)
			{
				if (ControllerLifetime.ActiveControllerType != ControllerType.Mouse)
				{
					EventSystem.current.SetSelectedGameObject(_window.firstButton.gameObject);
				}
				else
				{
					EventSystem.current.SetSelectedGameObject(null);
				}
			}
		}

		protected override void InitStateBehaviour()
		{
		}

		public override void Activate()
		{
			base.Activate();
			PauseManager.Instance.PauseGame();
		}

		public override void Deactivate()
		{
			base.Deactivate();
			PauseManager.Instance.ResumeGame();
			this.onDeactivate?.Invoke();
			this.onDeactivate = null;
			if (EventSystem.current != null)
			{
				EventSystem.current.SetSelectedGameObject(null);
			}
		}
	}
}
