using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Mouse
{
	[AddComponentMenu("QTI/Triggers/Mouse/MouseDownTrigger")]
	public class MouseDownTrigger : InteractionTrigger
	{
		public IInteractor interactor;

		private void OnMouseDown()
		{
			base.Interact(interactor);
		}
	}
}
