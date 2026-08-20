using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Mouse
{
	[AddComponentMenu("QTI/Triggers/Mouse/MouseExitTrigger")]
	public class MouseExitTrigger : InteractionTrigger
	{
		public IInteractor interactor;

		private void OnMouseExit()
		{
			base.Interact(interactor);
		}
	}
}
