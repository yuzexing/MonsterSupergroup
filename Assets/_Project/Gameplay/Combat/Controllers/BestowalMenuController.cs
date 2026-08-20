using System;
using AstralShift.Control;
using AstralShift.FSM;
using AstralShift.HellMaiden.Audio;
using AstralShift.HellMaiden.UI;
using AstralShift.Managers;
using AstralShift.Rendering;
using Rewired;
using UnityEngine.EventSystems;

namespace AstralShift.HellMaiden.Controllers
{
	public class BestowalMenuController : PerkMenuController
	{
		public event Action OnUISubmitPressed;

		public event Action OnControllerTypeChanged;

		public override void Activate()
		{
			MusicPlayer.Instance.SetSnapShot(MusicPlayer.SnapshotID.Menu);
			InputHandler.EnableMenuInputs();
			ASRendererFeature.Instance.EnableFullscreenBlurRenderPass(enable: true);
			MusicPlayer.Instance.SetSnapShot(MusicPlayer.SnapshotID.Card);
			ControllerLifetime.OnControllerChanged += PointerManager.Instance.SetUIPointer;
			ControllerLifetime.OnControllerChanged += OnControllerTypeChange;
			PointerManager.Instance.SetUIPointer();
			PauseManager.Instance.PauseGame();
			if (base.IsSuspended)
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
			ASRendererFeature.Instance.EnableFullscreenBlurRenderPass(enable: false);
			ControllerLifetime.OnControllerChanged -= PointerManager.Instance.SetUIPointer;
			ControllerLifetime.OnControllerChanged -= OnControllerTypeChange;
			PauseManager.Instance.ResumeGame();
		}

		protected override void InitializeStateMachine()
		{
			_stateMachine = new StateMachine("Perk Menu");
			_opening = new State("Opening");
			_closing = new State("Closing");
			_waitingForPick = new State("Waiting For Pick");
			_suspended = new State("Suspended");
			_opening.onEnter = delegate
			{
				base.Menu.EnableMenuInteraction(state: false);
			};
			_waitingForPick.onEnter = delegate
			{
				base.Menu.EnableMenuInteraction(state: true);
				base.Menu.RegisterAllActions();
				base.Menu.ShowDetailsMenuGlyph(state: true);
				OnControllerTypeChange();
			};
			_waitingForPick.onExit = delegate
			{
				base.Menu.EnableMenuInteraction(state: false);
				base.Menu.UnRegisterAllActions();
				base.Menu.ShowDetailsMenuGlyph(state: false);
				EventSystem.current.SetSelectedGameObject(null);
			};
			_suspended.onEnter = delegate
			{
				EventSystem.current.SetSelectedGameObject(null);
				base.Menu.EnableMenuInteraction(state: false);
			};
			_stateMachine.AddTransition(_opening, _closing);
			_stateMachine.AddTransition(_closing, _opening);
			_stateMachine.AddTransition(_opening, _waitingForPick);
			_stateMachine.AddTransition(_waitingForPick, _closing);
			_stateMachine.AddTransition(_waitingForPick, _suspended);
			_stateMachine.AddTransition(_suspended, _waitingForPick);
			_stateMachine.SetInitialState(_opening);
		}

		public override void OnDestroy()
		{
			ControllerManager.Instance.UnSubscribe(this);
		}

		public override void UICancel(InputActionEventData data)
		{
		}

		public override void UISubmit(InputActionEventData data)
		{
			if (data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnUISubmitPressed?.Invoke();
			}
		}

		public override void UIButton4(InputActionEventData data)
		{
		}

		public override void UICenter1(InputActionEventData data)
		{
			if (data.eventType == InputActionEventType.ButtonJustPressed)
			{
				OpenDetailsMenu();
			}
		}

		protected override void OnControllerTypeChange()
		{
			this.OnControllerTypeChanged?.Invoke();
		}
	}
}
