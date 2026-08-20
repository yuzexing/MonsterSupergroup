using System;
using System.Collections.Generic;

namespace AstralShift.Helpers.Collections
{
	public static class CollectionHelpers
	{
		public static void SwapNodes<T>(this LinkedListNode<T> nodeA, LinkedListNode<T> nodeB)
		{
			if (nodeA != null && nodeB != null && nodeA != nodeB)
			{
				T value = nodeB.Value;
				T value2 = nodeA.Value;
				T val = (nodeA.Value = value);
				val = (nodeB.Value = value2);
			}
		}

		public static void Shuffle<T>(this IList<T> list, Random generator = null)
		{
			if (generator == null)
			{
				generator = new Random();
			}
			for (int num = list.Count - 1; num > 0; num--)
			{
				int num2 = generator.Next(num + 1);
				int index = num;
				int index2 = num2;
				T val = list[num2];
				T val2 = list[num];
				T val3 = (list[index] = val);
				val3 = (list[index2] = val2);
			}
		}

		public static void AddIfNotNull<T>(this IList<T> list, T item)
		{
			if (item != null)
			{
				list.Add(item);
			}
		}
	}
}
