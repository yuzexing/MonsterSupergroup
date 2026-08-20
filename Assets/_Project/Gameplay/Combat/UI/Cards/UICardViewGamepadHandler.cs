using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Cards
{
	[RequireComponent(typeof(UICardViewHandler))]
	public abstract class UICardViewGamepadHandler : Selectable, ISubmitHandler, IEventSystemHandler
	{
		[SerializeField]
		[HideInInspector]
		protected UICardViewHandler viewHandler;

		protected override void Awake()
		{
			if (!viewHandler)
			{
				viewHandler = GetComponent<UICardViewHandler>();
				viewHandler.GamepadHandler = this;
			}
		}

		public abstract void OnSubmit(BaseEventData eventData);

		public override void OnPointerDown(PointerEventData eventData)
		{
		}

		public override void OnPointerUp(PointerEventData eventData)
		{
		}

		public override void OnPointerEnter(PointerEventData eventData)
		{
		}

		public override void OnPointerExit(PointerEventData eventData)
		{
		}

		public virtual void ConstructNavigation(Selectable left, Selectable right, Selectable top, Selectable bottom)
		{
			Navigation navigation = base.navigation;
			navigation.mode = Navigation.Mode.Explicit;
			navigation.wrapAround = false;
			navigation.selectOnLeft = left;
			navigation.selectOnRight = right;
			navigation.selectOnUp = top;
			navigation.selectOnDown = bottom;
			base.navigation = navigation;
		}

		public virtual void ClearNavigation()
		{
			Navigation navigation = base.navigation;
			navigation.mode = Navigation.Mode.Explicit;
			navigation.wrapAround = false;
			navigation.selectOnLeft = null;
			navigation.selectOnRight = null;
			navigation.selectOnUp = null;
			navigation.selectOnDown = null;
			base.navigation = navigation;
		}

		public virtual void SetLeftNavigation(Selectable left)
		{
			Navigation navigation = base.navigation;
			navigation.mode = Navigation.Mode.Explicit;
			navigation.wrapAround = false;
			navigation.selectOnLeft = left;
			base.navigation = navigation;
		}

		public virtual void SetRightNavigation(Selectable right)
		{
			Navigation navigation = base.navigation;
			navigation.mode = Navigation.Mode.Explicit;
			navigation.wrapAround = false;
			navigation.selectOnRight = right;
			base.navigation = navigation;
		}

		public virtual void SetUpNavigation(Selectable up)
		{
			Navigation navigation = base.navigation;
			navigation.mode = Navigation.Mode.Explicit;
			navigation.wrapAround = false;
			navigation.selectOnUp = up;
			base.navigation = navigation;
		}

		public virtual void SetDownNavigation(Selectable down)
		{
			Navigation navigation = base.navigation;
			navigation.mode = Navigation.Mode.Explicit;
			navigation.wrapAround = false;
			navigation.selectOnDown = down;
			base.navigation = navigation;
		}

		public abstract void ClearBindings();
	}
}
