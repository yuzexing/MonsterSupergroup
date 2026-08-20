using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.HellMaiden.UI.Cards
{
	[RequireComponent(typeof(UICardViewHandler))]
	public abstract class UICardViewMouseHandler : MonoBehaviour, IBeginDragHandler, IEventSystemHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
	{
		[SerializeField]
		[HideInInspector]
		protected UICardViewHandler viewHandler;

		protected virtual void Awake()
		{
			if (!viewHandler)
			{
				viewHandler = GetComponent<UICardViewHandler>();
				viewHandler.MouseHandler = this;
			}
		}

		public virtual void OnBeginDrag(PointerEventData eventData)
		{
		}

		public virtual void OnDrag(PointerEventData eventData)
		{
		}

		public virtual void OnEndDrag(PointerEventData eventData)
		{
		}

		public virtual void OnPointerEnter(PointerEventData eventData)
		{
		}

		public virtual void OnPointerExit(PointerEventData eventData)
		{
		}

		public virtual void OnPointerMove(PointerEventData eventData)
		{
		}
	}
}
