using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions
{
	[AddComponentMenu("QTI/Interactions/DestroyComponentInteraction")]
	public class DestroyComponentInteraction : Interaction
	{
		public Component[] toDestroy;

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
			OnEnd();
		}
	}
}
