using System;
using AstralShift.Control;
using AstralShift.Control.Controllers;
using AstralShift.HellMaiden.Audio;
using AstralShift.HellMaiden.UI;
using AstralShift.Managers;
using AstralShift.QTI.Helpers;
// using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace AstralShift.HellMaiden.Controllers
{
	public class DialogueController : UIController
	{
		public Action endDialogue;

		public override void Activate()
		{
			base.Activate();
			MusicPlayer.Instance.SetSnapShot(MusicPlayer.SnapshotID.Dialogue);
			// DialogueManager.Instance.transform.GetComponent<DialogueSystemEvents>().conversationEvents.onConversationEnd.AddListener(EndDialogue);
			ControllerLifetime.OnControllerChanged += PointerManager.Instance.SetUIPointer;
			PointerManager.Instance.SetUIPointer();
			PauseManager.Instance.PausePausables();
			GameDirector.Instance.Player.StopMovement();
		}

		public override void Deactivate()
		{
			base.Deactivate();
			// DialogueManager.Instance.transform.GetComponent<DialogueSystemEvents>().conversationEvents.onConversationEnd.RemoveListener(EndDialogue);
			ControllerLifetime.OnControllerChanged -= PointerManager.Instance.SetUIPointer;
			PauseManager.Instance.ResumePausables();
		}

		public void OnDestroy()
		{
			ControllerManager.Instance.UnSubscribe(this);
		}

		public void EndDialogue(Transform t)
		{
			StartCoroutine(Wait.SetUnscaledTimeout(0.5f, delegate
			{
				ControllerManager.Instance.YieldGameController();
				endDialogue?.Invoke();
				endDialogue = null;
			}));
		}
	}
}
