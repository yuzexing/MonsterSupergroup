using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions
{
	[AddComponentMenu("QTI/Interactions/ActivationInteraction")]
	public class ActivationInteraction : Interaction
	{
		[Tooltip("The activation state, true will set active and false will set inactive")]
		public bool Active = true;

		public GameObject[] toActivate;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			for (int i = 0; i < toActivate.Length; i++)
			{
				toActivate[i].SetActive(Active);
			}
			OnEnd();
		}
	}
}
