using UnityEngine;

namespace AstralShift.QTI.Triggers
{
	public class OnDistance2DTrigger : OnDistanceTrigger
	{
		protected override float CalculateDistance(Vector3 a, Vector3 b)
		{
			return Vector2.Distance(a, b);
		}
	}
}
