using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Lifecycle
{
	[AddComponentMenu("QTI/Triggers/Lifecycle/OnAwakeTrigger")]
	public class OnAwakeTrigger : InteractionTrigger
	{
		public IInteractor interactor;

		protected override void Awake()
		{
			base.Interact(interactor);
		}
	}
}
