using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Lifecycle
{
	[AddComponentMenu("QTI/Triggers/Lifecycle/OnDestroyTrigger")]
	public class OnDestroyTrigger : InteractionTrigger
	{
		public IInteractor interactor;

		private void OnDestroy()
		{
			base.Interact(interactor);
		}
	}
}
