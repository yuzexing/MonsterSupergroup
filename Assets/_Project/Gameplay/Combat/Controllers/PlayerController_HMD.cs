using System;
using AstralShift.Control;
using AstralShift.Control.Controllers;
using AstralShift.FSM;
using AstralShift.HellMaiden.Audio;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.DevDebug;
using AstralShift.HellMaiden.Scenes;
using AstralShift.HellMaiden.UI;
using AstralShift.Managers;
using Rewired;
using UnityEngine;

namespace AstralShift.HellMaiden.Controllers
{
	public class PlayerController_HMD : GameController
	{
		private Vector2 MovementDirection;

		private bool blockMovement;

		private Vector2 AimDirection = Vector2.right;

		private Vector2 AimPosition;

		private StateMachine _stateMachine;

		private State combat;

		private State hub;

		private State levelingUp;

		private State dying;

		private State fadingOut;

		private bool _enteredInCombatState;

		public bool InBusyState
		{
			get
			{
				if (_stateMachine.GetState() != levelingUp)
				{
					return _stateMachine.GetState() == dying;
				}
				return true;
			}
		}

		private bool InHubState => _stateMachine.GetState() == hub;

		private bool InCombatState => _stateMachine.GetState() == combat;

		public bool InLevelingUpState => _stateMachine.GetState() == levelingUp;

		protected void Start()
		{
			_stateMachine = new StateMachine("[PlayerController]");
			combat = new State("combat");
			hub = new State("hub");
			levelingUp = new State("levelingUp");
			dying = new State("dying");
			fadingOut = new State("fadingOut");
			_stateMachine.AddTransition(combat, levelingUp);
			_stateMachine.AddTransition(combat, fadingOut);
			_stateMachine.AddTransition(levelingUp, combat);
			_stateMachine.AddTransition(dying, fadingOut);
			_stateMachine.AddTransition(dying, hub);
			_stateMachine.AddTransition(fadingOut, combat);
			_stateMachine.AddTransition(fadingOut, hub);
			_stateMachine.AddTransition(hub, fadingOut);
			_stateMachine.AddTransition(combat, dying);
			_stateMachine.AddTransition(fadingOut, dying);
			_stateMachine.AddTransition(levelingUp, dying);
			// _stateMachine.SetInitialStateNoCallbacks((SceneMaster.Instance.CurrentSceneEnum == SceneEnum.Hub) ? hub : combat);
			State state = dying;
			state.onEnter = (Action)Delegate.Combine(state.onEnter, (Action)delegate
			{
				PauseManager.Instance.PausePausables();
			});
			State state2 = dying;
			state2.onExit = (Action)Delegate.Combine(state2.onExit, (Action)delegate
			{
				PauseManager.Instance.ResumePausables();
			});
			// GameEvents instance = GameEvents.Instance;
			// instance.OnLevelIncrease = (Action<int>)Delegate.Combine(instance.OnLevelIncrease, new Action<int>(TransitionToLevelingUp));
			// GameEvents instance2 = GameEvents.Instance;
			// instance2.OnLevelUp = (Action)Delegate.Combine(instance2.OnLevelUp, new Action(TransitionToCombat));
			// GameEvents instance3 = GameEvents.Instance;
			// instance3.OnBeforePlayerDeath = (Action)Delegate.Combine(instance3.OnBeforePlayerDeath, new Action(TransitionToDying));
			// SceneMaster.Instance.OnSceneHideStartPersist += TransitionToFadingOutState;
			// SceneMaster.Instance.OnSceneHideFinishPersist += TransitionToCombatOrHub;
		}

		protected void OnDestroy()
		{
			// GameEvents instance = GameEvents.Instance;
			// instance.OnLevelIncrease = (Action<int>)Delegate.Remove(instance.OnLevelIncrease, new Action<int>(TransitionToLevelingUp));
			// GameEvents instance2 = GameEvents.Instance;
			// instance2.OnLevelUp = (Action)Delegate.Remove(instance2.OnLevelUp, new Action(TransitionToCombat));
			// GameEvents instance3 = GameEvents.Instance;
			// instance3.OnBeforePlayerDeath = (Action)Delegate.Remove(instance3.OnBeforePlayerDeath, new Action(TransitionToDying));
			// SceneMaster.Instance.OnSceneHideStartPersist -= TransitionToFadingOutState;
			// SceneMaster.Instance.OnSceneHideFinishPersist -= TransitionToCombatOrHub;
			ControllerManager.Instance.UnSubscribe(this);
		}

		public override void Activate()
		{
			// MusicPlayer.Instance.SetSnapShot(MusicPlayer.SnapshotID.Normal);
			base.Activate();
			ResetInputValues();
			// GameDirector.Instance.Player.ResetInputDirection();
			if (InCombatState)
			{
				// ControllerLifetime.OnControllerChanged += PointerManager.Instance.SetBattlePointer;
				// PointerManager.Instance.SetBattlePointer();
				// if (CombatUIManager.Instance != null)
				// {
				// 	CombatUIManager.Instance.OpenHUD();
				// }
				_enteredInCombatState = true;
			}
			else
			{
				// PointerManager.Instance.HideMouseCursor();
			}
			BlockMovement(state: false);
			// GameDirector.Instance.Player.EnableInteractor();
		}

		public override void Deactivate()
		{
			if (_enteredInCombatState)
			{
				// ControllerLifetime.OnControllerChanged -= PointerManager.Instance.SetBattlePointer;
				// if (CombatUIManager.Instance != null)
				// {
				// 	CombatUIManager.Instance.CloseHUD();
				// }
				_enteredInCombatState = false;
			}
			BlockMovement(state: true);
			GameDirector.Instance.Player.DisableInteractor();
		}

		private void BlockMovement(bool state)
		{
			blockMovement = state;
		}

		private void ResetInputValues()
		{
			MovementDirection = Vector2.zero;
			AimDirection = Vector2.right;
			AimPosition = Vector2.right;
		}

		public override void RightStickHorizontal(InputActionEventData data)
		{
			AimDirection = new Vector2(data.GetAxis(), AimDirection.y);
			GameDirector.Instance.Player.SetAimDirection(AimDirection);
		}

		public override void RightStickVertical(InputActionEventData data)
		{
			AimDirection = new Vector2(AimDirection.x, data.GetAxis());
			GameDirector.Instance.Player.SetAimDirection(AimDirection);
		}

		public override void MousePosition(Vector2 value)
		{
			AimPosition = value;
			GameDirector.Instance.Player.SetAimPosition(AimPosition);
		}

		public override void RightTrigger(InputActionEventData data)
		{
			if (!InBusyState && data.GetButton())
			{
				GameDirector.Instance.Player.Dash();
			}
		}

		public override void Center1(InputActionEventData data)
		{
			if (!InBusyState && !InHubState && data.eventType == InputActionEventType.ButtonJustPressed && CombatUIManager.Instance != null)
			{
				CombatUIManager.Instance.OpenStatsMenu();
			}
		}

		public override void Center2(InputActionEventData data)
		{
			if (!InBusyState && data.eventType == InputActionEventType.ButtonJustPressed && !PauseMenuController.blockOpenAction)
			{
				ControllerManager.Instance.OverrideGameController<PauseMenuController>();
			}
		}

		public override void Button1(InputActionEventData data)
		{
			if (data.eventType == InputActionEventType.ButtonJustPressed)
			{
				GameDirector.Instance.Player.Interact();
			}
		}

		public override void Button2(InputActionEventData data)
		{
			if (!InBusyState && !InHubState && data.eventType == InputActionEventType.ButtonJustPressed)
			{
				GameDirector.Instance.Player.UltimateAction();
			}
		}

		public override void Button3(InputActionEventData data)
		{
		}

		public override void LeftStickHorizontal(InputActionEventData data)
		{
			if (blockMovement)
			{
				MovementDirection = new Vector2(0f, 0f);
				GameDirector.Instance.Player.SetDirection(MovementDirection);
				return;
			}
			if (data.IsCurrentInputSource(ControllerType.Joystick))
			{
				MovementDirection = new Vector2(data.GetAxis(), MovementDirection.y);
				MovementDirection = Vector2.ClampMagnitude(MovementDirection, 1f);
			}
			else
			{
				MovementDirection = new Vector2(data.GetAxisRaw(), MovementDirection.y);
				MovementDirection.Normalize();
			}
			GameDirector.Instance.Player.SetDirection(MovementDirection);
		}

		public override void LeftStickVertical(InputActionEventData data)
		{
			if (blockMovement)
			{
				MovementDirection = new Vector2(0f, 0f);
				GameDirector.Instance.Player.SetDirection(MovementDirection);
				return;
			}
			if (data.IsCurrentInputSource(ControllerType.Joystick))
			{
				MovementDirection = new Vector2(MovementDirection.x, data.GetAxis());
				MovementDirection = Vector2.ClampMagnitude(MovementDirection, 1f);
			}
			else
			{
				MovementDirection = new Vector2(MovementDirection.x, data.GetAxisRaw() * 0.5f);
				MovementDirection.Normalize();
			}
			GameDirector.Instance.Player.SetDirection(MovementDirection);
		}

		private void TransitionToCombat()
		{
			_stateMachine.MakeTransition(combat);
		}

		private void TransitionToCombatOrHub()
		{
			if (SceneMaster.Instance.NextSceneEnum == SceneEnum.Hub)
			{
				_stateMachine.MakeTransition(hub);
			}
			else
			{
				_stateMachine.MakeTransition(combat);
			}
		}

		private void TransitionToDying()
		{
			_stateMachine.MakeTransition(dying);
		}

		private void TransitionToLevelingUp(int _)
		{
			_stateMachine.MakeTransition(levelingUp);
		}

		private void TransitionToFadingOutState()
		{
			_stateMachine.MakeTransition(fadingOut);
		}

		public override void DebugAction1Pressed(InputActionEventData data)
		{
			if (DeveloperDebug.devMode)
			{
				DeveloperDebug.DebugIncreaseHealth();
			}
		}

		public override void DebugAction2Pressed(InputActionEventData data)
		{
		}

		public override void DebugAction3Pressed(InputActionEventData data)
		{
			if (DeveloperDebug.devMode)
			{
				DeveloperDebug.DebugEnemyDamageSwitch();
			}
		}
	}
}
