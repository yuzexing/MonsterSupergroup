using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions.DebugInteractions
{
	[AddComponentMenu("QTI/Interactions/Debug/DebugLogInteraction")]
	public class DebugLogInteraction : Interaction
	{
		public string debugString = "";

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			MonoBehaviour.print("TRIGGERED Debug Interaction: " + debugString);
			OnEnd();
		}
	}
}
