using System;
using FMODUnity;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.UI.PopupWindows
{
	public class PopupWindowButton : PopupWindowComponent
	{
		[SerializeField]
		private Button button;

		private PopupWindow _parentWindow;

		public EventReference buttonSound;

		public Button GetButton()
		{
			return button;
		}

		public void Init()
		{
			_parentWindow = GetComponent<PopupWindow>();
		}

		public override void SetContext(PopupContext context)
		{
			if (context.Actions.Count > index)
			{
				Action action = delegate
				{
					PopupWindow parentWindow = _parentWindow;
					parentWindow.onAfterClose = (Action)Delegate.Combine(parentWindow.onAfterClose, context.Actions[index]);
				};
				button.onClick.AddListener(action.Invoke);
			}
			button?.onClick.AddListener(_parentWindow.Close);
			button?.onClick.AddListener(PlayPressedSound);
		}

		private void PlayPressedSound()
		{
			RuntimeManager.PlayOneShot(buttonSound);
		}

		public override void ClearContext()
		{
			button?.onClick.RemoveAllListeners();
		}
	}
}
