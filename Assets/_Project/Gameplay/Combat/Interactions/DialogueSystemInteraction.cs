using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Characters;
using AstralShift.HellMaiden.Characters.Effects;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Dialogue;
using AstralShift.HellMaiden.Player;
using AstralShift.Managers;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
// using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class DialogueSystemInteraction : Interaction
	{
		[SerializeField]
		// private DialogueSystemTrigger dialogueSystemTrigger;

		// [ConversationPopup(false, false)]
		// public string conversation;

		public bool showNPCBaloon;

		public GameObject NPC;

		public bool overrideDialogueSettings;

		public DialogueOverrides dialogueOverrides;

		private CharacterBalloonController _NPCBalloon;

		private List<string> switchedActors;

		private CharacterMovement characterMovement;

		private PlayerMovement playerMovement;

		private void Start()
		{
			if (showNPCBaloon && NPC != null)
			{
				_NPCBalloon = NPC.GetComponentInChildren<CharacterBalloonController>();
				if ((bool)_NPCBalloon)
				{
					_NPCBalloon.DisplayBalloon(show: true, CharacterBalloonController.BalloonType.ExclamationMark);
				}
				_NPCBalloon.DisplayBalloon(show: true, CharacterBalloonController.BalloonType.ExclamationMark);
				characterMovement = NPC.GetComponent<CharacterMovement>();
				playerMovement = GameDirector.Instance.Player;
			}
		}

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			// dialogueSystemTrigger.conversation = conversation;
			// GameDataManager.RegisterDialogue(conversation);
			ControllerManager.Instance.OverrideGameController<DialogueController>();
			DialogueController dialogueController = ControllerManager.Instance.CurrentController as DialogueController;
			if (NPC != null && playerMovement != null)
			{
				Vector2 vector = NPC.transform.position - playerMovement.transform.position;
				characterMovement.FacingDirection = -vector;
				playerMovement.FacingDirection = vector;
			}
			if (showNPCBaloon && _NPCBalloon != null)
			{
				_NPCBalloon.DisplayBalloon(show: false);
			}
			// if (overrideDialogueSettings && dialogueOverrides.actorOverrides.Length != 0)
			{
				SetCharacterPanels();
				dialogueController.endDialogue = (Action)Delegate.Combine(dialogueController.endDialogue, new Action(ResetCharacterPanels));
			}
			dialogueController.endDialogue = (Action)Delegate.Combine(dialogueController.endDialogue, new Action(VerifyDialogueAchivements));
			dialogueController.endDialogue = (Action)Delegate.Combine(dialogueController.endDialogue, new Action(base.OnEnd));
			// AstralDialogueManager.Instance.SetDialogueMode(AstralDialogueManager.DialogueMode.Normal);
			// StartCoroutine(dialogueSystemTrigger.StartAtEndOfFrame());
		}

		private void SetCharacterPanels()
		{
			switchedActors = new List<string>();
			// for (int i = 0; i < dialogueOverrides.actorOverrides.Length; i++)
			// {
				// AstralDialogueManager.Instance.SetActorStageSide(dialogueOverrides.actorOverrides[i], AstralDialogueManager.StageSide.Left);
				// switchedActors.Add(dialogueOverrides.actorOverrides[i]);
			// }
		}

		private void ResetCharacterPanels()
		{
			foreach (string switchedActor in switchedActors)
			{
				// AstralDialogueManager.Instance.SetActorStageSide(switchedActor, AstralDialogueManager.StageSide.Right);
			}
		}

		public void SetDialogueOverrides(DialogueOverrides dialogueOverrides)
		{
			this.dialogueOverrides = dialogueOverrides;
			overrideDialogueSettings = true;
		}

		private void VerifyDialogueAchivements()
		{
			// string text = conversation.Substring(conversation.LastIndexOf('/') + 1);
			// if (text == "DLG_HMRxHOR_C1_02")
			// {
			// 	AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.ForumDweller);
			// }
			// if (text == "DLG_HMRxHORxOVI_01")
			// {
			// 	AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.NovicePoet);
			// }
		}
	}
}
