using System;

namespace AstralShift.Helpers
{
	public static class RandomHelpers
	{
		private static Random _random;

		public static int GetRandomInt(int min = 0, int maxExclusive = 2)
		{
			if (_random == null)
			{
				_random = new Random();
			}
			return _random.Next(min, maxExclusive);
		}

		public static float GetRandomFloat(float min = 0f, float max = 1f)
		{
			if (_random == null)
			{
				_random = new Random();
			}
			return (float)_random.NextDouble() * (max - min) + min;
		}
	}
}
