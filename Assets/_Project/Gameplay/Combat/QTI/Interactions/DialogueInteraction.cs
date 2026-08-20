using AstralShift.QTI.Interactors;
using TMPro;
using UnityEngine;

namespace AstralShift.QTI.Interactions
{
	[AddComponentMenu("QTI/Interactions/DialogueInteraction")]
	public class DialogueInteraction : Interaction
	{
		public TextMeshProUGUI textField;

		[TextArea(3, 3)]
		public string text;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			if (textField != null)
			{
				textField.text = text;
			}
			OnEnd();
		}
	}
}
