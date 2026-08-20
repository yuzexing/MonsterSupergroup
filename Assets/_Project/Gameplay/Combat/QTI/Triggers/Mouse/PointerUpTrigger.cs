using AstralShift.QTI.Interactors;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.QTI.Triggers.Mouse
{
	[AddComponentMenu("QTI/Triggers/Mouse/PointerUpTrigger")]
	public class PointerUpTrigger : InteractionTrigger, IPointerUpHandler, IEventSystemHandler, IPointerDownHandler
	{
		public IInteractor interactor;

		public void OnPointerDown(PointerEventData eventData)
		{
		}

		public void OnPointerUp(PointerEventData eventData)
		{
			base.Interact(interactor);
		}
	}
}
