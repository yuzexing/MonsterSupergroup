using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.QTI.Helpers
{
	public static class GameObjectHelpers
	{
		public static T[] GetAllComponentsOfTypeInScene<T>(GameObject firstReference, bool includeInactive = false) where T : Component
		{
			List<GameObject> list = new List<GameObject>();
			list.AddRange(firstReference.gameObject.scene.GetRootGameObjects());
			List<T> list2 = new List<T>();
			foreach (GameObject item in list)
			{
				list2.AddRange(item.GetComponentsInChildren<T>(includeInactive));
			}
			return list2.ToArray();
		}
	}
}
