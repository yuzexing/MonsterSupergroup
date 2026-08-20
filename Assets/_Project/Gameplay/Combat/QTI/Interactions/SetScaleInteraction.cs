using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions
{
	public class SetScaleInteraction : Interaction
	{
		public Transform targetObject;

		public Vector3 newScale = Vector3.one;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			if (targetObject != null)
			{
				targetObject.localScale = newScale;
			}
			else
			{
				Debug.LogError("SetScaleInteraction: targetObject is null!");
			}
			OnEnd();
		}
	}
}
