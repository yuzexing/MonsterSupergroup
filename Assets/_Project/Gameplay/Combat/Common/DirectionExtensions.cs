using UnityEngine;

namespace AstralShift.HellMaiden.Common
{
	public static class DirectionExtensions
	{
		public static Vector2 ToVector2(this Direction Direction)
		{
			int num = 0;
			int num2 = 0;
			if (Direction.HasFlag(Direction.Up))
			{
				num2++;
			}
			if (Direction.HasFlag(Direction.Down))
			{
				num2--;
			}
			if (Direction.HasFlag(Direction.Left))
			{
				num--;
			}
			if (Direction.HasFlag(Direction.Right))
			{
				num++;
			}
			return new Vector2(num, num2).normalized;
		}
	}
}
