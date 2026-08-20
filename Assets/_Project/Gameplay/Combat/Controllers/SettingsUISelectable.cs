using AstralShift.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public abstract class SettingsUISelectable : UISelectable
{
	public bool applyable;

	public bool isOnConsole = true;

	[SerializeField]
	private string description;

	public string Description => description;

	protected void Navigate(AxisEventData eventData, Selectable sel)
	{
		if (sel != null && sel.IsActive())
		{
			eventData.selectedObject = sel.gameObject;
		}
	}
}
