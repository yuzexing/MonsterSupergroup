using TMPro;
using UnityEngine;

namespace AstralShift.UI.PopupWindows
{
	public class PopupWindowText : PopupWindowComponent
	{
		[SerializeField]
		private TMP_Text text;

		public TMP_Text Text => text;

		public override void SetContext(PopupContext context)
		{
			if (context.Texts.Count > index)
			{
				text.text = context.Texts[index];
			}
		}
	}
}
