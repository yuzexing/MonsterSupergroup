using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Mouse
{
	[AddComponentMenu("QTI/Triggers/Mouse/MouseDragTrigger")]
	public class MouseDragTrigger : InteractionTrigger
	{
		public IInteractor interactor;

		private void OnMouseDrag()
		{
			base.Interact(interactor);
		}
	}
}
