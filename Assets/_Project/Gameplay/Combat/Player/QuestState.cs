using System;

namespace PixelCrushers.DialogueSystem
{
	[Flags]
	public enum QuestState
	{
		Unassigned = 1,
		Active = 2,
		Success = 4,
		Failure = 8,
		Abandoned = 0x10,
		Grantable = 0x20,
		ReturnToNPC = 0x40
	}
}
