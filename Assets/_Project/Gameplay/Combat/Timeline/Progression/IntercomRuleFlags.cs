using System;

namespace AstralShift.HellMaiden.Dialogue
{
	[Flags]
	public enum IntercomRuleFlags
	{
		None = 0,
		BlockInQuest = 1,
		BlockIfBusy = 2,
		BlockIfLeveling = 4
	}
}
