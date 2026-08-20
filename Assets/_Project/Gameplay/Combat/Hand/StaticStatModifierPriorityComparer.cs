using System.Collections.Generic;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public class StaticStatModifierPriorityComparer : IComparer<StaticStatModifier>
	{
		public static readonly StaticStatModifierPriorityComparer Instance = new StaticStatModifierPriorityComparer();

		public int Compare(StaticStatModifier x, StaticStatModifier y)
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
			return x.GetSortPriority().CompareTo(y.GetSortPriority());
		}
	}
}
