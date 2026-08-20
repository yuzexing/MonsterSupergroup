using System;
using Rewired;

namespace AstralShift.HellMaiden.Controllers
{
	public class NoMovementPlayerController : PlayerController_HMD
	{
		public Action onDeactivate;

		public override void LeftStickHorizontal(InputActionEventData data)
		{
		}

		public override void LeftStickVertical(InputActionEventData data)
		{
		}

		public override void RightTrigger(InputActionEventData data)
		{
		}

		public override void Deactivate()
		{
			base.Deactivate();
			onDeactivate?.Invoke();
		}
	}
}
