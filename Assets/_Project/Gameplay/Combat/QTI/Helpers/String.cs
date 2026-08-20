using System;

namespace AstralShift.QTI.Helpers
{
	public static class String
	{
		public static string ReplaceFirstOccurrence(string Source, string Find, string Replace)
		{
			int startIndex = Source.IndexOf(Find);
			return Source.Remove(startIndex, Find.Length).Insert(startIndex, Replace);
		}

		public static int StringToInt(string value)
		{
			int.TryParse(value, out var result);
			return result;
		}

		public static string Reverse(this string value)
		{
			char[] array = value.ToCharArray();
			Array.Reverse(array);
			return new string(array);
		}
	}
}
