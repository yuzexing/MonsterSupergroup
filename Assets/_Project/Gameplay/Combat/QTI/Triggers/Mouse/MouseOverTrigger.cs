using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Mouse
{
	[AddComponentMenu("QTI/Triggers/Mouse/MouseOverTrigger")]
	public class MouseOverTrigger : InteractionTrigger
	{
		public IInteractor interactor;

		private void OnMouseOver()
		{
			base.Interact(interactor);
		}
	}
}
