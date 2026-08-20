using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Mouse
{
	[AddComponentMenu("QTI/Triggers/Mouse/MouseUpAsButtonTrigger")]
	public class MouseUpAsButtonTrigger : InteractionTrigger
	{
		public IInteractor interactor;

		private void OnMouseUpAsButton()
		{
			base.Interact(interactor);
		}
	}
}
