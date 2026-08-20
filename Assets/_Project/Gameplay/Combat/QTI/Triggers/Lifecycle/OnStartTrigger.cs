using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Lifecycle
{
	[AddComponentMenu("QTI/Triggers/Lifecycle/OnStartTrigger")]
	public class OnStartTrigger : InteractionTrigger
	{
		public IInteractor interactor;

		private void Start()
		{
			base.Interact(interactor);
		}
	}
}
