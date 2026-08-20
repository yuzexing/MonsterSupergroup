using AstralShift.QTI.Interactors;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.QTI.Triggers.Mouse
{
	[AddComponentMenu("QTI/Triggers/Mouse/PointerDragTrigger")]
	public class PointerDragTrigger : InteractionTrigger, IDragHandler, IEventSystemHandler, IBeginDragHandler, IEndDragHandler
	{
		public enum EventType
		{
			BeginDrag = 0,
			Drag = 1,
			EndDrag = 2
		}

		public IInteractor interactor;

		public EventType triggerOn;

		public void OnBeginDrag(PointerEventData eventData)
		{
			if (triggerOn == EventType.BeginDrag)
			{
				base.Interact(interactor);
			}
		}

		public void OnDrag(PointerEventData eventData)
		{
			if (triggerOn == EventType.Drag)
			{
				base.Interact(interactor);
			}
		}

		public void OnEndDrag(PointerEventData eventData)
		{
			if (triggerOn == EventType.EndDrag)
			{
				base.Interact(interactor);
			}
		}
	}
}
