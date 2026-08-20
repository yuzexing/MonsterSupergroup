using UnityEngine;

namespace AstralShift.UI.PopupWindows
{
	public abstract class PopupWindowComponent : MonoBehaviour
	{
		[SerializeField]
		protected int index;

		public int Index => index;

		public abstract void SetContext(PopupContext context);

		public virtual void ClearContext()
		{
		}
	}
}
