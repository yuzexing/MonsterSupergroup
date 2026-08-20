using UnityEngine;

namespace AstralShift.UI.PopupWindows
{
	public class PopupWindowAnimator : PopupWindowComponent
	{
		public Animator[] Animators;

		public override void SetContext(PopupContext context)
		{
			if (context.Texts.Count > index)
			{
				for (int i = 0; i < Animators.Length; i++)
				{
					Animators[i].SetTrigger(context.Texts[index]);
				}
			}
		}
	}
}
