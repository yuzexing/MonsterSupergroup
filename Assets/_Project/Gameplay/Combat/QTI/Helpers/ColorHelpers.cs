using UnityEngine;

namespace AstralShift.QTI.Helpers
{
	public static class ColorHelpers
	{
		public static string ToHexString(this Color c)
		{
			return $"#{(int)c.r:X2}{(int)c.g:X2}{(int)c.b:X2}";
		}

		public static string ToRgbString(this Color c)
		{
			return $"RGB({c.r}, {c.g}, {c.b})";
		}
	}
}
