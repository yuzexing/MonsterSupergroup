using System.Collections.Generic;

namespace AstralShift.HellMaiden.Combat
{
	public class ProgressionStack : Stack<ProgressionTimeline>
	{
		public new void Push(ProgressionTimeline timeline)
		{
			if (TryPeek(out var result))
			{
				result.PauseAllMilestones();
			}
			base.Push(timeline);
		}

		public new void Pop()
		{
			if (base.Count != 0)
			{
				base.Pop();
				if (TryPeek(out var result))
				{
					result.ResumeAllMilestones();
				}
			}
		}

		public new void Clear()
		{
			base.Clear();
		}
	}
}
