using System;
using AstralShift.Control;
using AstralShift.Control.Controllers;
using AstralShift.FSM;
using AstralShift.HellMaiden.UI;
using AstralShift.HellMaiden.UI.Menus;
using AstralShift.Managers;
using Cysharp.Threading.Tasks;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.HellMaiden.Controllers
{
	public class WeaponSelectionMenuController : UIController
	{
		[SerializeField]
		protected WeaponSelectionMenuView menuView;

		private StateMachine _globalStateMachine;

		private State _opening;

		private State _waitingForSelection;

		private State _closing;

		public WeaponSelectionMenuView MenuView => menuView;

		public bool IsOpening
		{
			get
			{
				if (_globalStateMachine != null)
				{
					return _globalStateMachine.GetState() == _closing;
				}
				return false;
			}
		}

		public bool IsClosing
		{
			get
			{
				if (_globalStateMachine != null)
				{
					return _globalStateMachine.GetState() == _closing;
				}
				return false;
			}
		}

		public bool IsActive
		{
			get
			{
				if (_globalStateMachine != null && _globalStateMachine.GetState() != _opening && _globalStateMachine.GetState() != _closing)
				{
					return menuView.IsInteractable;
				}
				return false;
			}
		}

		public bool IsWaitingForSelection
		{
			get
			{
				if (_globalStateMachine != null)
				{
					return _globalStateMachine.GetState() == _waitingForSelection;
				}
				return false;
			}
		}

		public event Func<UniTask> OnUIDirectionalLeftPressed;

		public event Func<UniTask> OnUIDirectionalRightPressed;

		public event Action<InputActionEventData> OnUILeftAnalogHorizontal;

		public event Action<InputActionEventData> OnUILeftAnalogVertical;

		public event Action<InputActionEventData> OnUIRightAnalogHorizontal;

		public event Action<InputActionEventData> OnUIRightAnalogVertical;

		public override void Activate()
		{
			base.Activate();
			ControllerLifetime.OnControllerChanged += PointerManager.Instance.SetUIPointer;
			ControllerLifetime.OnControllerChanged += OnControllerTypeChange;
			PointerManager.Instance.SetUIPointer();
			PauseManager.Instance.PauseGame();
			InitializeStateMachine();
		}

		public override void Deactivate()
		{
			base.Deactivate();
			ControllerLifetime.OnControllerChanged -= PointerManager.Instance.SetUIPointer;
			ControllerLifetime.OnControllerChanged -= OnControllerTypeChange;
			PauseManager.Instance.ResumeGame();
		}

		private void InitializeStateMachine()
		{
			if (_globalStateMachine == null)
			{
				_globalStateMachine = new StateMachine("Weapon Selection Menu - Global State");
				_opening = new State("Opening");
				_waitingForSelection = new State("Waiting For Weapon Selection");
				_closing = new State("Closing");
				State opening = _opening;
				opening.onEnter = (Action)Delegate.Combine(opening.onEnter, (Action)delegate
				{
					ClearBindings();
					menuView.EnableMenuInteraction(state: false);
				});
				State waitingForSelection = _waitingForSelection;
				waitingForSelection.onEnter = (Action)Delegate.Combine(waitingForSelection.onEnter, (Action)delegate
				{
					menuView.RegisterScrollBindings();
					menuView.EnableMenuInteraction(state: true);
					SetFirstSelection();
				});
				State closing = _closing;
				closing.onEnter = (Action)Delegate.Combine(closing.onEnter, (Action)delegate
				{
					EventSystem.current.SetSelectedGameObject(null);
					menuView.EnableMenuInteraction(state: false);
				});
				_globalStateMachine.AddTransition(_opening, _waitingForSelection);
				_globalStateMachine.AddTransition(_waitingForSelection, _closing);
				_globalStateMachine.AddTransition(_closing, _opening);
				_globalStateMachine.SetInitialState(_opening);
			}
			else
			{
				TransitionToOpen();
			}
		}

		public void TransitionToOpen()
		{
			_globalStateMachine.MakeTransition(_opening);
		}

		public void TransitionToWaitingForSelection()
		{
			_globalStateMachine.MakeTransition(_waitingForSelection);
		}

		public void TransitionToClose()
		{
			_globalStateMachine.MakeTransition(_closing);
		}

		public override void UIDirectionalLeft(InputActionEventData data)
		{
			if (menuView.IsInteractable && data.eventType == InputActionEventType.NegativeButtonJustPressed)
			{
				this.OnUIDirectionalLeftPressed?.Invoke();
			}
		}

		public override void UIDirectionalRight(InputActionEventData data)
		{
			if (menuView.IsInteractable && data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnUIDirectionalRightPressed?.Invoke();
			}
		}

		public override void UILeftStickHorizontal(InputActionEventData data)
		{
			if (menuView.IsInteractable)
			{
				this.OnUILeftAnalogHorizontal?.Invoke(data);
			}
		}

		public override void UILeftStickVertical(InputActionEventData data)
		{
			if (menuView.IsInteractable)
			{
				this.OnUILeftAnalogVertical?.Invoke(data);
			}
		}

		public override void UIRightStickHorizontal(InputActionEventData data)
		{
			if (menuView.IsInteractable)
			{
				this.OnUIRightAnalogHorizontal?.Invoke(data);
			}
		}

		public override void UIRightStickVertical(InputActionEventData data)
		{
			if (menuView.IsInteractable)
			{
				this.OnUIRightAnalogVertical?.Invoke(data);
			}
		}

		public override void UIButton3(InputActionEventData data)
		{
			if (menuView.IsInteractable && data.eventType == InputActionEventType.ButtonJustPressed)
			{
				menuView.ToggleInfoPanel();
			}
		}

		public void SetFirstSelection()
		{
			menuView.SelectFocusedWeapon();
		}

		public async void OnControllerTypeChange()
		{
			if (IsActive)
			{
				await UniTask.NextFrame();
				menuView.SelectFocusedWeapon();
			}
		}

		protected void ClearBindings()
		{
			menuView.UnRegisterScrollBindings();
			this.OnUIDirectionalLeftPressed = null;
			this.OnUIDirectionalRightPressed = null;
			this.OnUILeftAnalogHorizontal = null;
			this.OnUILeftAnalogVertical = null;
			this.OnUIRightAnalogHorizontal = null;
			this.OnUIRightAnalogVertical = null;
		}
	}
}
