using System.Collections.Generic;
using AstralShift.Helpers;
// using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace AstralShift.HellMaiden.Helpers
{
	public class DialogueLineTrigger : MonoBehaviour
	{
		[SerializeField]
		private string _eventName = "event:/sx/dlg/sx_dlg_vo";

		[SerializeField]
		private List<string> _lines = new List<string>();

		[SerializeField]
		private bool _playOnEnable = true;

		private void OnEnable()
		{
			if (_playOnEnable)
			{
				Play();
			}
		}

		public void Play()
		{
			// DialogueManager.instance.GetComponent<FmodProgramerEventPlayer>().PlayRandomDialogueFromList(_eventName, _lines, 1f);
		}
	}
}
