using System.Linq;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyFinder : MonoBehaviour
	{
		public LayerMask layerMask;

		public EnemyController FindClosest(float radius)
		{
			Collider2D[] array = Physics2D.OverlapCircleAll(base.transform.position, radius, layerMask);
			if (array.Length == 0)
			{
				return null;
			}
			EnemyController[] array2 = new EnemyController[array.Length];
			for (int i = 0; i < array.Length; i++)
			{
				array2[i] = array[i].GetComponentInParent<EnemyController>();
			}
			array2 = array2.OrderBy((EnemyController e) => e.DistanceToTarget).ToArray();
			string text = "";
			for (int num = 0; num < array2.Length; num++)
			{
				text = text + array2[num].DistanceToTarget + "||";
			}
			MonoBehaviour.print(text);
			return array2[0];
		}
	}
}
