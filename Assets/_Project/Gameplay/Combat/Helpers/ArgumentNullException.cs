using System;

namespace AstralShift.Helpers
{
	internal static class ArgumentNullException
	{
		public static void ThrowIfNull(object o)
		{
			if (o == null)
			{
				throw new System.ArgumentNullException();
			}
		}
	}
}
