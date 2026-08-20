using UnityEngine;

namespace AstralShift.Helpers
{
	public static class PositionHelpers
	{
		public static bool IsPositionBetween(Vector3 position, Vector3 positionA, Vector3 positionB)
		{
			Vector3 vector = positionB - positionA;
			float num = Vector3.Dot(position - positionA, vector.normalized);
			if (num > 0f)
			{
				return num < vector.magnitude;
			}
			return false;
		}

		public static bool IsPositionInCone(Vector2 origin, Vector2 forwardDirection, Vector2 position, float coneAngle, float maxDistance)
		{
			Vector2 rhs = position - origin;
			if (rhs.magnitude > maxDistance)
			{
				return false;
			}
			rhs.Normalize();
			return Mathf.Acos(Vector2.Dot(forwardDirection.normalized, rhs)) * 57.29578f <= coneAngle / 2f;
		}

		public static void DrawPoint(Vector3 position, Color color, float size = 0.3f, float duration = 3f)
		{
			float num = size * 0.5f;
			Vector3 vector = new Vector3(num, 0f, 0f);
			Vector3 vector2 = new Vector3(0f, num, 0f);
			Quaternion quaternion = Quaternion.Euler(0f, 0f, 45f);
			vector = quaternion * vector;
			vector2 = quaternion * vector2;
			Debug.DrawLine(position - vector, position + vector, color, duration, depthTest: false);
			Debug.DrawLine(position - vector2, position + vector2, color, duration, depthTest: false);
		}
	}
}
