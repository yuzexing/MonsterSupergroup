using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Spawners.SpawnShapes
{
	public class CircleSpawnShape : SpawnShape
	{
		public float radius = 5f;

		[Range(1f, 20f)]
		public float aspectRatio = 1f;

		public override Vector2 GetEnemyPosition(Vector2 center, int count, int idx)
		{
			float f = (float)idx * MathF.PI * 2f / (float)count;
			float x = Mathf.Cos(f) * radius;
			float num = Mathf.Sin(f) * radius;
			return center + new Vector2(x, num / aspectRatio);
		}

		public override bool ValidVertexCount(int count)
		{
			return true;
		}
	}
}
