using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.MapGeneration
{
	public class PropAsset : MonoBehaviour
	{
		[SerializeField]
		private BoxCollider2D boundaries;

		[SerializeField]
		private bool isProcedural;

		[ConditionalHide("isProcedural", true)]
		[SerializeField]
		private PropSpawner propProcedural;

		[SerializeField]
		public bool isReplaceable;

		[ConditionalHide("isReplaceable", true)]
		public ReplaceablePropSize propSize;

		private Bounds _bounds;

		[Header("!!! DANGER ZONE !!!")]
		public bool alsoSetToZero = true;

		public float Width;

		public float Height;

		public Vector2 minBounds;

		public Bounds GetBounds()
		{
			_bounds = boundaries.bounds;
			return _bounds;
		}

		public Vector2 GetOffsetedPosition(Vector2 targetPosition)
		{
			Vector2 vector = minBounds;
			Vector2 vector2 = (Vector2)base.transform.position - vector;
			return targetPosition + vector2;
		}

		public void ProceduralGenerate()
		{
			if (isProcedural && propProcedural != null)
			{
				propProcedural.SpawnProps();
			}
		}
	}
}
