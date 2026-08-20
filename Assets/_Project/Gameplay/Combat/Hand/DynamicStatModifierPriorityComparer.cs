using System.Collections.Generic;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public class DynamicStatModifierPriorityComparer : IComparer<DynamicStatModifier>
	{
		public static readonly DynamicStatModifierPriorityComparer Instance = new DynamicStatModifierPriorityComparer();

		public int Compare(DynamicStatModifier x, DynamicStatModifier y)
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
