using AstralShift.QTI.Interactors;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.QTI.Triggers.Mouse
{
	[AddComponentMenu("QTI/Triggers/Mouse/PointerDropTrigger")]
	public class PointerDropTrigger : InteractionTrigger, IDropHandler, IEventSystemHandler
	{
		public IInteractor interactor;

		public void OnDrop(PointerEventData eventData)
		{
			base.Interact(interactor);
		}
	}
}
