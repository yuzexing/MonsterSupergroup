using System;

namespace AstralShift.HellMaiden.MapGeneration
{
	[Serializable]
	public class IntDistanceConstraint
	{
		public IntPair key;

		public int distance;

		public IntDistanceConstraint(int first, int second, int distance)
		{
			key = new IntPair(first, second);
			this.distance = distance;
		}
	}
}
