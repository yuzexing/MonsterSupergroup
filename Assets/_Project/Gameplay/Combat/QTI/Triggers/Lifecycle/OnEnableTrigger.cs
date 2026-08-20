using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Lifecycle
{
	[AddComponentMenu("QTI/Triggers/Lifecycle/OnEnableTrigger")]
	public class OnEnableTrigger : InteractionTrigger
	{
		public IInteractor interactor;

		private void OnEnable()
		{
			Interact(interactor);
		}

		public override void Interact(IInteractor interactor)
		{
			if (!(interaction == null))
			{
				interaction.Interact(interactor);
			}
		}
	}
}
