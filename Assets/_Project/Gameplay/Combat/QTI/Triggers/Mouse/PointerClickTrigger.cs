using AstralShift.QTI.Interactors;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.QTI.Triggers.Mouse
{
	[AddComponentMenu("QTI/Triggers/Mouse/PointerClickTrigger")]
	public class PointerClickTrigger : InteractionTrigger, IPointerClickHandler, IEventSystemHandler
	{
		public IInteractor interactor;

		public void OnPointerClick(PointerEventData eventData)
		{
			base.Interact(interactor);
		}
	}
}
