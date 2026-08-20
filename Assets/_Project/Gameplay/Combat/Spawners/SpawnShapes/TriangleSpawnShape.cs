using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Spawners.SpawnShapes
{
	public class TriangleSpawnShape : SpawnShape
	{
		public int objectsPerSide = 5;

		public float triangleSize = 5f;

		public Vector3 centerPosition = Vector3.zero;

		public override Vector2 GetEnemyPosition(Vector2 center, int count, int idx)
		{
			if (count % 3 != 0)
			{
				Debug.LogWarning("Can't spawn in triangle shape when count isn't divisible by 3!");
				return Vector2.zero;
			}
			objectsPerSide = count / 3;
			Vector3[] obj = new Vector3[3]
			{
				center + new Vector2((0f - triangleSize) / 2f, (0f - Mathf.Sqrt(3f)) * triangleSize / 6f),
				center + new Vector2(triangleSize / 2f, (0f - Mathf.Sqrt(3f)) * triangleSize / 6f),
				center + new Vector2(0f, Mathf.Sqrt(3f) * triangleSize / 3f)
			};
			int num = idx / objectsPerSide;
			Vector2 vector = obj[num];
			Vector2 vector2 = obj[(num + 1) % 3];
			int i = idx - num * objectsPerSide;
			return SpawnObjectsAlongLine(vector, vector2, objectsPerSide, i);
		}

		private Vector2 SpawnObjectsAlongLine(Vector3 start, Vector3 end, int count, int i)
		{
			float t = (float)i / (float)count;
			return Vector2.Lerp(start, end, t);
		}

		public override bool ValidVertexCount(int count)
		{
			return count % 3 == 0;
		}
	}
}
