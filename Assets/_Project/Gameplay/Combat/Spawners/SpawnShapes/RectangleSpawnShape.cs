using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Spawners.SpawnShapes
{
	public class RectangleSpawnShape : SpawnShape
	{
		public int objectsPerSide = 5;

		public float width = 5f;

		public float height = 3f;

		public Vector2 centerPosition = Vector2.zero;

		public override Vector2 GetEnemyPosition(Vector2 center, int count, int idx)
		{
			if (count % 4 != 0)
			{
				Debug.LogWarning("Can't spawn in rectangle shape when count isn't divisible by 4!");
				return Vector2.zero;
			}
			objectsPerSide = count / 4;
			Vector2 vector = center + new Vector2((0f - width) / 2f, height / 2f);
			Vector2 vector2 = center + new Vector2(width / 2f, height / 2f);
			Vector2 vector3 = center + new Vector2((0f - width) / 2f, (0f - height) / 2f);
			Vector2 vector4 = center + new Vector2(width / 2f, (0f - height) / 2f);
			int num = idx / objectsPerSide;
			int idx2 = idx - num * objectsPerSide;
			switch (num)
			{
			default:
				Debug.LogWarning("SpawnShape: Error calculating position.");
				return SpawnObjectsAlongLine(vector, vector2, objectsPerSide, idx2);
			case 0:
				return SpawnObjectsAlongLine(vector, vector2, objectsPerSide, idx2);
			case 1:
				return SpawnObjectsAlongLine(vector2, vector4, objectsPerSide, idx2);
			case 2:
				return SpawnObjectsAlongLine(vector4, vector3, objectsPerSide, idx2);
			case 3:
				return SpawnObjectsAlongLine(vector3, vector, objectsPerSide, idx2);
			}
		}

		private Vector2 SpawnObjectsAlongLine(Vector2 start, Vector2 end, int count, int idx)
		{
			float t = (float)idx / (float)count;
			return Vector2.Lerp(start, end, t);
		}

		public override bool ValidVertexCount(int count)
		{
			return count % 4 == 0;
		}
	}
}
