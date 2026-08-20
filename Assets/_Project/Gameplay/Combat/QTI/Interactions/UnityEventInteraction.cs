using AstralShift.QTI.Interactors;
using UnityEngine;
using UnityEngine.Events;

namespace AstralShift.QTI.Interactions
{
	[AddComponentMenu("QTI/Interactions/UnityEventInteraction")]
	public class UnityEventInteraction : Interaction
	{
		public UnityEvent UnityEvent;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			UnityEvent.Invoke();
			OnEnd();
		}
	}
}
