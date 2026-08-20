using System;
using AstralShift.HellMaiden.Helpers;
using AstralShift.Helpers;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class TeleportMovement : EnemyDefaultMovement
	{
		[SerializeField]
		private LayerMask obstaclesLayerMask;

		public bool useCameraBoundsToTeleport;

		public float minTeleportDistance = 5f;

		public float maxTeleportDistance = 10f;

		public float minDistanceFromCameraEdge = 5f;

		public float minDistanceFromPlayer = 5f;

		private Vector3 nextTeleportPosition;

		private EnemyController controller;

		private void Start()
		{
			controller = enemyController as EnemyController;
		}

		public override void MovementUpdate()
		{
			if ((bool)_transform && _canMove && controller.StateMachine.PreviousState == controller.Recovery)
			{
				_canMove = !TeleportToPlayerRadius();
				controller.transform.position = nextTeleportPosition;
			}
		}

		private bool TeleportToPlayerRadius()
		{
			Vector3 position = controller.Target.transform.position;
			if (!useCameraBoundsToTeleport)
			{
				for (int i = 0; i < 100; i++)
				{
					float f = UnityEngine.Random.Range(0f, MathF.PI * 2f);
					float num = UnityEngine.Random.Range(minTeleportDistance, maxTeleportDistance);
					Vector3 vector = new Vector3(Mathf.Cos(f), Mathf.Sin(f), 0f) * num;
					Vector2 vector2 = position + vector;
					if (SpawnHelpers.GetSpawnLocationIsValid(vector2, controller.spawnReferenceRadius, obstaclesLayerMask) && ProCamera2DHelpers.IsWithinCameraBounds(vector2))
					{
						nextTeleportPosition = vector2;
						return true;
					}
				}
			}
			else
			{
				Vector2 cameraExtents = ProCamera2DHelpers.GetCameraExtents();
				Vector2 vector3 = Camera.main.transform.position;
				float minInclusive = vector3.x - cameraExtents.x + minDistanceFromCameraEdge;
				float maxInclusive = vector3.x + cameraExtents.x - minDistanceFromCameraEdge;
				float minInclusive2 = vector3.y - cameraExtents.y + minDistanceFromCameraEdge;
				float maxInclusive2 = vector3.y + cameraExtents.y - minDistanceFromCameraEdge;
				Vector2 zero = Vector2.zero;
				for (int j = 0; j < 100; j++)
				{
					zero = new Vector2(UnityEngine.Random.Range(minInclusive, maxInclusive), UnityEngine.Random.Range(minInclusive2, maxInclusive2));
					if (Vector2.Distance(zero, position) >= minDistanceFromPlayer)
					{
						nextTeleportPosition = zero;
						return true;
					}
				}
			}
			return false;
		}
	}
}
