using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Menus
{
	[RequireComponent(typeof(WSMCardViewHandlerContainer))]
	public class WSMCardViewHandlerContainerNavigation : Selectable
	{
		private WSMCardViewHandlerContainer _container;

		private int _currentElementIndex;

		protected override void Awake()
		{
			base.Awake();
			TryGetComponent<WSMCardViewHandlerContainer>(out _container);
		}

		public override void OnMove(AxisEventData eventData)
		{
		}

		public void SelectCurrent()
		{
			_container.SelectFocusedElement();
		}

		public void SelectNext()
		{
			_container.ScrollToLeft();
			_container.SelectFocusedElement();
		}

		public void SelectPrevious()
		{
			_container.ScrollToRight();
			_container.SelectFocusedElement();
		}
	}
}
