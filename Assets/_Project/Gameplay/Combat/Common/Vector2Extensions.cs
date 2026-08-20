using UnityEngine;

namespace AstralShift.HellMaiden.Common
{
	public static class Vector2Extensions
	{
		public static Direction ToDirection(this Vector2 vector2)
		{
			vector2 = vector2.normalized;
			Direction direction = Direction.None;
			if (vector2.x < -0.25f)
			{
				direction |= Direction.Left;
			}
			else if (vector2.x > 0.25f)
			{
				direction |= Direction.Right;
			}
			if (vector2.y < -0.25f)
			{
				direction |= Direction.Down;
			}
			else if (vector2.y > 0.25f)
			{
				direction |= Direction.Up;
			}
			return direction;
		}
	}
}
