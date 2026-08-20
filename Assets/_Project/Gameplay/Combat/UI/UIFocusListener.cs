using UnityEngine;

namespace AstralShift.HellMaiden.UI
{
	public abstract class UIFocusListener : MonoBehaviour
	{
		public abstract void OnFocusEnter();

		public abstract void OnFocusExit();
	}
}
