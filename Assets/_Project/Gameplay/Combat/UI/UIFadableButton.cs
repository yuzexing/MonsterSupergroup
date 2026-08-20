using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralShift.UI
{
	[RequireComponent(typeof(Button))]
	public class UIFadableButton : UIFadable
	{
		protected Button _button;

		public Button Button
		{
			get
			{
				if (_button == null)
				{
					TryGetComponent<Button>(out _button);
				}
				return _button;
			}
		}

		public async void SimulateClick()
		{
			PointerEventData eventData = new PointerEventData(EventSystem.current);
			ExecuteEvents.Execute(Button.gameObject, eventData, ExecuteEvents.pointerDownHandler);
			ExecuteEvents.Execute(Button.gameObject, eventData, ExecuteEvents.submitHandler);
			ExecuteEvents.Execute(Button.gameObject, eventData, ExecuteEvents.pointerUpHandler);
		}
	}
}
