using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Lifecycle
{
	[AddComponentMenu("QTI/Triggers/Lifecycle/OnDisableTrigger")]
	public class OnDisableTrigger : InteractionTrigger
	{
		public IInteractor interactor;

		private void OnDisable()
		{
			base.Interact(interactor);
		}
	}
}
