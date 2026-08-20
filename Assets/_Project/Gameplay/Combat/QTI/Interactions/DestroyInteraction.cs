using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions
{
	[AddComponentMenu("QTI/Interactions/DestroyInteraction")]
	public class DestroyInteraction : Interaction
	{
		public GameObject[] toDestroy;

		public bool alsoDestroyInteractor;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			for (int i = 0; i < toDestroy.Length; i++)
			{
				if (toDestroy[i] != null)
				{
					Object.Destroy(toDestroy[i]);
				}
			}
			if (alsoDestroyInteractor && interactor != null && interactor.Transform != null)
			{
				Object.Destroy(interactor.Transform.gameObject);
			}
			OnEnd();
		}
	}
}
