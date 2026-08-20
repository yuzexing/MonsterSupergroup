using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Mouse
{
	[AddComponentMenu("QTI/Triggers/Mouse/MouseUpTrigger")]
	public class MouseUpTrigger : InteractionTrigger
	{
		public IInteractor interactor;

		private void OnMouseUp()
		{
			base.Interact(interactor);
		}
	}
}
