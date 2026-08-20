using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Mouse
{
	[AddComponentMenu("QTI/Triggers/Mouse/MouseEnterTrigger")]
	public class MouseEnterTrigger : InteractionTrigger
	{
		public IInteractor interactor;

		private void OnMouseEnter()
		{
			base.Interact(interactor);
		}
	}
}
