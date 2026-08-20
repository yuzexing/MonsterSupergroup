using System.Collections.Generic;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public class OnKillModifierPriorityComparer : IComparer<OnKillModifier>
	{
		public static readonly OnKillModifierPriorityComparer Instance = new OnKillModifierPriorityComparer();

		public int Compare(OnKillModifier x, OnKillModifier y)
		{
			if (x == y)
			{
				return 0;
			}
			if (x == null)
			{
				return -1;
			}
			if (y == null)
			{
				return 1;
			}
			int num = x.GetSortPriority().CompareTo(y.GetSortPriority());
			if (num != 0)
			{
				return num;
			}
			return y.GetRollPriority().CompareTo(x.GetRollPriority());
		}
	}
}
