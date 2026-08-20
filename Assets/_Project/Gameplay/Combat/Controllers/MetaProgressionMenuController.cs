using System;
using AstralShift.Control;
using AstralShift.Control.Controllers;
using AstralShift.FSM;
using AstralShift.HellMaiden.UI;
using AstralShift.HellMaiden.UI.Menus.MetaProgression;
using AstralShift.Helpers;
using AstralShift.Managers;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.HellMaiden.Controllers
{
	public class MetaProgressionMenuController : GameMenuController
	{
		[SerializeField]
		protected MetaProgressionMenuView view;

		private bool _canExit;

		protected void Start()
		{
			if (view == null)
			{
				view = GetComponent<MetaProgressionMenuView>();
			}
			ControllerManager.Instance.Subscribe(this, init: true);
			view.Init();
			view.Animancer = menuAnimator;
		}

		protected override void InitStateBehaviour()
		{
			State active = Active;
			active.onEnter = (Action)Delegate.Combine(active.onEnter, new Action(view.OnOpen));
			State closing = Closing;
			closing.onExit = (Action)Delegate.Combine(closing.onExit, new Action(view.OnClose));
		}

		public override void Activate()
		{
			base.Activate();
			ControllerLifetime.OnControllerChanged += PointerManager.Instance.SetPointerForMenuNavigation;
			ControllerLifetime.OnControllerChanged += OnControllerTypeChange;
			GameDirector.Instance.Player.StopMovement();
		}

		public override void Deactivate()
		{
			base.Deactivate();
			ControllerLifetime.OnControllerChanged -= PointerManager.Instance.SetPointerForMenuNavigation;
			ControllerLifetime.OnControllerChanged -= OnControllerTypeChange;
		}

		public void EnableExit()
		{
			_canExit = true;
		}

		public void DisableExit()
		{
			_canExit = false;
		}

		public override void UICancelPressed(InputActionEventData data)
		{
			if (_canExit)
			{
				Close();
				view.Close();
				ControllerManager.Instance.YieldGameController();
			}
		}

		public override void UIButton4(InputActionEventData data)
		{
			base.UIButton4(data);
			if (data.eventType == InputActionEventType.ButtonJustReleased)
			{
				TimerHoldInteractionTaskHelper.CancelAndDispose();
			}
			if (data.eventType == InputActionEventType.ButtonPressed)
			{
				TimerHoldInteractionTaskHelper.ProcessHoldAsync(view.skipHoldTime, view.Refund);
			}
		}

		private void OnControllerTypeChange()
		{
			if (ControllerLifetime.ActiveControllerType != ControllerType.Mouse)
			{
				view.SetCurrentSelection();
			}
			else
			{
				EventSystem.current.SetSelectedGameObject(null);
			}
		}

		private void OnDestroy()
		{
			ControllerManager.Instance.UnSubscribe(this);
		}

		protected override void OnClosingFinished()
		{
			base.OnClosingFinished();
			EventSystem.current.SetSelectedGameObject(null);
		}
	}
}
