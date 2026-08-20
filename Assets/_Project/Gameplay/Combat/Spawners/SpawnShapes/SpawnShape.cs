using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Spawners.SpawnShapes
{
	public abstract class SpawnShape : MonoBehaviour
	{
		public abstract Vector2 GetEnemyPosition(Vector2 center, int count, int idx);

		public abstract bool ValidVertexCount(int count);
	}
}
