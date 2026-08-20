using Rewired;
using UnityEngine;

namespace AstralShift.Control.Controllers
{
	public abstract class GameController : MonoBehaviour
	{
		protected virtual void OnEnable()
		{
		}

		protected virtual void OnDisable()
		{
		}

		public virtual void Init()
		{
		}

		public virtual void Activate()
		{
			InputHandler.EnableNormalInputs();
		}

		public abstract void Deactivate();

		public void DisablePlayerControls()
		{
		}

		public void EnablePlayerControls()
		{
		}

		public virtual void AnyInputDown()
		{
		}

		public virtual void AnyMouseInputStateChanged(int button, bool pressed)
		{
		}

		public virtual void Button1(InputActionEventData data)
		{
		}

		public virtual void Button2(InputActionEventData data)
		{
		}

		public virtual void Button3(InputActionEventData data)
		{
		}

		public virtual void Button4(InputActionEventData data)
		{
		}

		public virtual void UIAccept(InputActionEventData data)
		{
		}

		public virtual void LeftStickHorizontal(InputActionEventData data)
		{
		}

		public virtual void LeftStickVertical(InputActionEventData data)
		{
		}

		public virtual void LeftStickButton(InputActionEventData data)
		{
		}

		public virtual void RightStickHorizontal(InputActionEventData data)
		{
		}

		public virtual void RightStickVertical(InputActionEventData data)
		{
		}

		public virtual void RightStickButton(InputActionEventData data)
		{
		}

		public virtual void DirectionalUp(InputActionEventData data)
		{
		}

		public virtual void DirectionalDown(InputActionEventData data)
		{
		}

		public virtual void DirectionalLeft(InputActionEventData data)
		{
		}

		public virtual void DirectionalRight(InputActionEventData data)
		{
		}

		public virtual void LeftShoulder(InputActionEventData data)
		{
		}

		public virtual void RightShoulder(InputActionEventData data)
		{
		}

		public virtual void LeftTrigger(InputActionEventData data)
		{
		}

		public virtual void RightTrigger(InputActionEventData data)
		{
		}

		public virtual void Center1(InputActionEventData data)
		{
		}

		public virtual void Center2(InputActionEventData data)
		{
		}

		public virtual void MouseHorizontal(InputActionEventData data)
		{
		}

		public virtual void MouseVertical(InputActionEventData data)
		{
		}

		public virtual void MouseLeftButton(InputActionEventData data)
		{
		}

		public virtual void MouseRightButton(InputActionEventData data)
		{
		}

		public virtual void MouseWheel(InputActionEventData data)
		{
		}

		public virtual void MousePosition(Vector2 value)
		{
		}

		public virtual void UISubmit(InputActionEventData data)
		{
		}

		public virtual void UIVertical(InputActionEventData data)
		{
		}

		public virtual void UICancel(InputActionEventData data)
		{
		}

		public virtual void UICancelPressed(InputActionEventData data)
		{
		}

		public virtual void UICancelHeld(InputActionEventData data)
		{
		}

		public virtual void UICancelReleased(InputActionEventData data)
		{
		}

		public virtual void UIButton3(InputActionEventData data)
		{
		}

		public virtual void UIButton4(InputActionEventData data)
		{
		}

		public virtual void UIDirectionalRight(InputActionEventData data)
		{
		}

		public virtual void UIDirectionalLeft(InputActionEventData data)
		{
		}

		public virtual void UIDirectionalUp(InputActionEventData data)
		{
		}

		public virtual void UIDirectionalDown(InputActionEventData data)
		{
		}

		public virtual void UILeftStickHorizontal(InputActionEventData data)
		{
		}

		public virtual void UILeftStickVertical(InputActionEventData data)
		{
		}

		public virtual void UIRightStickHorizontal(InputActionEventData data)
		{
		}

		public virtual void UIRightStickVertical(InputActionEventData data)
		{
		}

		public virtual void UIRightTrigger(InputActionEventData data)
		{
		}

		public virtual void UILeftTrigger(InputActionEventData data)
		{
		}

		public virtual void UICenter1(InputActionEventData data)
		{
		}

		public virtual void UICenter2(InputActionEventData data)
		{
		}

		public virtual void DebugAction1Pressed(InputActionEventData data)
		{
		}

		public virtual void DebugAction2Pressed(InputActionEventData data)
		{
		}

		public virtual void DebugAction3Pressed(InputActionEventData data)
		{
		}
	}
}
