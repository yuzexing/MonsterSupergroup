using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.UI.PopupWindows
{
	public class PopupWindowImage : PopupWindowComponent
	{
		[SerializeField]
		private Image image;

		public override void SetContext(PopupContext context)
		{
			if (context.Sprites.Count > index)
			{
				image.sprite = context.Sprites[index];
			}
		}
	}
}
