using System;
// using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	[Serializable]
	public struct DialogueLUTEntry
	{
		// [Tooltip("The conversation to start.")]
		// [ConversationPopup(false, false)]
		// public string conversation;

		public int priority;

		public bool isRewatchable;

		public bool overrideDialogueSettings;

		public DialogueOverrides DialogueOverrides;

		public DialogueLUTDialogueDependency[] dialogueDependencies;

		public DialogueLUTTriggerDependency[] triggerDependencies;

		public DialogueLUTNumberDependency[] numberDependencies;

		public CutsceneLUT.CutsceneLUTDependency[] cutsceneDependencies;
	}
}
