using System;

namespace AstralShift.HellMaiden.Data.Cards
{
	[Serializable]
	public abstract class DataDependency
	{
		public enum Comparison
		{
			Equal = 0,
			Greater = 1,
			Smaller = 2,
			GreaterOrEqual = 3,
			SmallerOrEqual = 4,
			NotEqual = 5
		}
	}
}
