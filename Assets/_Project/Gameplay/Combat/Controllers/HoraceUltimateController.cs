using AstralShift.Control;
using AstralShift.Control.Controllers;
using AstralShift.HellMaiden.Player.Attacks.HoraceAttacks;
using AstralShift.HellMaiden.UI;
using AstralShift.Managers;
using Rewired;
using UnityEngine;

namespace Assets.Scripts.AstralShift.HellMaiden.Controllers
{
	public class HoraceUltimateController : GameController
	{
		public HoraceUltimateAttack ultimateAttack;

		private Vector2 MovementDirection;

		private void Awake()
		{
			ControllerManager.Instance.Subscribe(this, init: true);
		}

		public override void Activate()
		{
			base.Activate();
			ControllerLifetime.OnControllerChanged += PointerManager.Instance.SetBattlePointer;
			PointerManager.Instance.SetBattlePointer();
			if (CombatUIManager.Instance != null)
			{
				CombatUIManager.Instance.OpenHUD();
			}
			if (ultimateAttack.Started)
			{
				ultimateAttack.ReturnFromInterrupt();
			}
		}

		public override void Deactivate()
		{
			ControllerLifetime.OnControllerChanged -= PointerManager.Instance.SetBattlePointer;
			if (CombatUIManager.Instance != null)
			{
				CombatUIManager.Instance.CloseHUD();
			}
		}

		public override void LeftStickHorizontal(InputActionEventData data)
		{
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
			ultimateAttack.SetHorizontalDirection(MovementDirection);
		}

		public override void Center1(InputActionEventData data)
		{
			if (data.eventType == InputActionEventType.ButtonJustPressed && CombatUIManager.Instance != null)
			{
				CombatUIManager.Instance.OpenStatsMenu();
			}
		}

		public override void Center2(InputActionEventData data)
		{
			if (data.eventType == InputActionEventType.ButtonJustPressed && !PauseMenuController.blockOpenAction)
			{
				ControllerManager.Instance.OverrideGameController<PauseMenuController>();
			}
		}

		private void OnDestroy()
		{
			ControllerManager.Instance.UnSubscribe(this);
		}
	}
}
