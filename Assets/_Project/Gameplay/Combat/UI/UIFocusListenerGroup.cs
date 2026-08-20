using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.HellMaiden.UI
{
	public class UIFocusListenerGroup : UIFocusListener
	{
		[SerializeField]
		private UIBaseFocusElement parentFocus;

		[SerializeField]
		private List<UIFocusListener> listeners;

		private void Awake()
		{
			parentFocus.OnFocusGained += OnFocusEnter;
			parentFocus.OnFocusLost += OnFocusExit;
		}

		private void OnDestroy()
		{
			parentFocus.OnFocusGained -= OnFocusEnter;
			parentFocus.OnFocusLost -= OnFocusExit;
		}

		public override void OnFocusEnter()
		{
			foreach (UIFocusListener listener in listeners)
			{
				listener.OnFocusEnter();
			}
		}

		public override void OnFocusExit()
		{
			foreach (UIFocusListener listener in listeners)
			{
				listener.OnFocusExit();
			}
		}
	}
}
