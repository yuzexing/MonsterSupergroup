using System;
using System.Collections.Generic;
using System.Linq;

namespace AstralShift.QTI.Helpers
{
	public static class Collections
	{
		public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source, Random generator = null)
		{
			if (generator == null)
			{
				generator = new Random();
			}
			T[] elements = source.ToArray();
			for (int i = elements.Length - 1; i >= 0; i--)
			{
				int swapIndex = generator.Next(i + 1);
				yield return elements[swapIndex];
				elements[swapIndex] = elements[i];
			}
		}

		public static bool HaveSameElements<T>(T[] array1, T[] array2)
		{
			if (array1 == null || array2 == null)
			{
				if (array1 == null)
				{
					return array2 == null;
				}
				return false;
			}
			if (array1.Length != array2.Length)
			{
				return false;
			}
			T[] first = array1.OrderBy((T item) => item).ToArray();
			T[] second = array2.OrderBy((T item) => item).ToArray();
			return first.SequenceEqual(second);
		}
	}
}
