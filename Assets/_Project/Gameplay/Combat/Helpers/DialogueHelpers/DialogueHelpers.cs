// using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace AstralShift.Helpers.DialogueHelpers
{
	public static class DialogueHelpers
	{
		// public static Subtitle GetBarkSubtitle(string conversationTitle, int entryID, Transform speaker, Transform listener)
		// {
		// 	Conversation conversation = DialogueManager.masterDatabase.GetConversation(conversationTitle);
		// 	if (conversation == null)
		// 	{
		// 		return null;
		// 	}
		// 	DialogueEntry dialogueEntry = conversation.GetDialogueEntry(entryID);
		// 	if (dialogueEntry == null)
		// 	{
		// 		return null;
		// 	}
		// 	ConversationModel conversationModel = new ConversationModel(DialogueManager.masterDatabase, conversationTitle, speaker, listener, allowLuaExceptions: true, null);
		// 	PixelCrushers.DialogueSystem.CharacterInfo characterInfo = conversationModel.GetCharacterInfo(dialogueEntry.ActorID, speaker);
		// 	PixelCrushers.DialogueSystem.CharacterInfo characterInfo2 = conversationModel.GetCharacterInfo(dialogueEntry.ConversantID, listener);
		// 	FormattedText formattedText = FormattedText.Parse(dialogueEntry.currentDialogueText);
		// 	Lua.Run(dialogueEntry.userScript);
		// 	return new Subtitle(characterInfo, characterInfo2, formattedText, dialogueEntry.Sequence, string.Empty, dialogueEntry);
		// }
	}
}
