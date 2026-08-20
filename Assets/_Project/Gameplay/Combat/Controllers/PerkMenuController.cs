using System;
using AstralShift.Control;
using AstralShift.Control.Controllers;
using AstralShift.FSM;
using AstralShift.HellMaiden.Audio;
using AstralShift.HellMaiden.UI;
using AstralShift.HellMaiden.UI.Menus;
using AstralShift.Managers;
using Cysharp.Threading.Tasks;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.HellMaiden.Controllers
{
	public class PerkMenuController : UIController
	{
		[SerializeField]
		public PerkMenuView menu;

		[SerializeField]
		protected float confirmHoldTime = 0.5f;

		[SerializeField]
		protected float confirmRerollTime = 0.5f;

		[SerializeField]
		protected float onSuspensionDelay = 0.5f;

		[SerializeField]
		protected float banishPerkHoldTime = 2f;

		protected StateMachine _stateMachine;

		protected State _opening;

		protected State _closing;

		protected State _waitingForPick;

		protected State _rerolling;

		protected State _suspended;

		public PerkMenuView Menu => menu;

		public float ConfirmHoldTime => confirmHoldTime;

		public float ConfirmRerollTime => confirmRerollTime;

		public float BanishPerkHoldTime => banishPerkHoldTime;

		public bool IsSuspended
		{
			get
			{
				if (_stateMachine != null)
				{
					return _stateMachine.GetState() == _suspended;
				}
				return false;
			}
		}

		public bool IsActive
		{
			get
			{
				if (_stateMachine != null && _stateMachine.GetState() == _waitingForPick)
				{
					return Menu.IsInteractable;
				}
				return false;
			}
		}

		public event Action<float> OnUICenter2Hold;

		public event Action<float> OnUIButton4Hold;

		public event Action OnUIButton4Pressed;

		public event Action OnUIButton4Released;

		public event Action<float> OnUICancelPressed;

		public override void Activate()
		{
			ControllerLifetime.EnableMouseDeadzone = true;
			base.Activate();
			MusicPlayer.Instance.SetSnapShot(MusicPlayer.SnapshotID.Card);
			ControllerLifetime.OnControllerChanged += PointerManager.Instance.SetUIPointer;
			ControllerLifetime.OnControllerChanged += OnControllerTypeChange;
			PointerManager.Instance.SetUIPointer();
			PauseManager.Instance.PauseGame();
			if (IsSuspended)
			{
				ReturnFromSuspended().Forget();
			}
			else if (_stateMachine == null)
			{
				InitializeStateMachine();
			}
			else
			{
				TransitionToOpen();
			}
		}

		public override void Deactivate()
		{
			ControllerLifetime.EnableMouseDeadzone = false;
			base.Deactivate();
			ControllerLifetime.OnControllerChanged -= PointerManager.Instance.SetUIPointer;
			ControllerLifetime.OnControllerChanged -= OnControllerTypeChange;
			PauseManager.Instance.ResumeGame();
		}

		protected virtual void InitializeStateMachine()
		{
			_stateMachine = new StateMachine("Perk Menu");
			_opening = new State("Opening");
			_closing = new State("Closing");
			_waitingForPick = new State("Waiting For Pick");
			_rerolling = new State("Rerolling");
			_suspended = new State("Suspended");
			_opening.onEnter = delegate
			{
				Menu.EnableMenuInteraction(state: false);
			};
			_waitingForPick.onEnter = delegate
			{
				Menu.EnableMenuInteraction(state: true);
				Menu.RegisterAllActions();
				Menu.ShowDetailsMenuGlyph(state: true);
				OnControllerTypeChange();
			};
			_waitingForPick.onExit = delegate
			{
				Menu.EnableMenuInteraction(state: false);
				Menu.UnRegisterAllActions();
				Menu.ShowDetailsMenuGlyph(state: false);
				EventSystem.current.SetSelectedGameObject(null);
			};
			_suspended.onEnter = delegate
			{
				EventSystem.current.SetSelectedGameObject(null);
				Menu.EnableMenuInteraction(state: false);
			};
			_stateMachine.AddTransition(_opening, _closing);
			_stateMachine.AddTransition(_closing, _opening);
			_stateMachine.AddTransition(_opening, _waitingForPick);
			_stateMachine.AddTransition(_waitingForPick, _closing);
			_stateMachine.AddTransition(_waitingForPick, _rerolling);
			_stateMachine.AddTransition(_rerolling, _waitingForPick);
			_stateMachine.AddTransition(_rerolling, _closing);
			_stateMachine.AddTransition(_waitingForPick, _suspended);
			_stateMachine.AddTransition(_suspended, _waitingForPick);
			_stateMachine.SetInitialState(_opening);
		}

		protected void TransitionToOpen()
		{
			_stateMachine.MakeTransition(_opening);
		}

		public void TransitionToClose()
		{
			_stateMachine.MakeTransition(_closing);
		}

		public void TransitionToWaitingForPick()
		{
			_stateMachine.MakeTransition(_waitingForPick);
		}

		public void TransitionToReRolling()
		{
			_stateMachine.MakeTransition(_rerolling);
		}

		protected void TransitionToSuspended()
		{
			_stateMachine.MakeTransition(_suspended);
		}

		protected virtual async UniTaskVoid ReturnFromSuspended()
		{
			await UniTask.Delay((int)(onSuspensionDelay * 1000f), ignoreTimeScale: true);
			TransitionToWaitingForPick();
		}

		protected virtual void OnControllerTypeChange()
		{
			if (IsActive)
			{
				if (ControllerLifetime.ActiveControllerType != ControllerType.Mouse)
				{
					Menu.SetCurrentSelection();
				}
				else
				{
					EventSystem.current.SetSelectedGameObject(null);
				}
			}
		}

		public virtual void OnDestroy()
		{
			ControllerManager.Instance.UnSubscribe(this);
		}

		public override void UICenter2(InputActionEventData data)
		{
			if (data.eventType == InputActionEventType.ButtonPressed)
			{
				this.OnUICenter2Hold?.Invoke((float)data.GetButtonTimePressed());
			}
		}

		public override void UICancel(InputActionEventData data)
		{
			if (data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnUICancelPressed?.Invoke((float)data.GetButtonTimePressed());
			}
		}

		public override void UIButton4(InputActionEventData data)
		{
			if (data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnUIButton4Pressed?.Invoke();
			}
			else if (data.eventType == InputActionEventType.ButtonJustReleased)
			{
				this.OnUIButton4Released?.Invoke();
			}
			else if (data.eventType == InputActionEventType.ButtonPressed)
			{
				this.OnUIButton4Hold?.Invoke((float)data.GetButtonTimePressed());
			}
		}

		public override void UICenter1(InputActionEventData data)
		{
			if (data.eventType == InputActionEventType.ButtonJustPressed)
			{
				OpenDetailsMenu();
			}
		}

		protected virtual void OpenDetailsMenu()
		{
			if (IsActive && CombatUIManager.Instance != null)
			{
				TransitionToSuspended();
				CombatUIManager.Instance.OpenStatsMenu(1, instant: true);
			}
		}
	}
}
