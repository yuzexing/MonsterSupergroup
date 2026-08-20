using AstralShift.Helpers;
using Pathfinding;
using UnityEngine;

namespace AstralShift.HellMaiden.Helpers
{
	public static class SpawnHelpers
	{
		private const int GetSpawnPositionMaxIterations = 100;

		public static bool GetOffScreenSpawnPosition(float spawnReferenceRadius, Bounds bounds, float unitsFromCamera, LayerMask layerMask, out Vector2 spawnPosition)
		{
			for (int i = 0; i < 100; i++)
			{
				spawnPosition = ProCamera2DHelpers.GetPointOutsideCamera(unitsFromCamera, bounds);
				if (AstarPath.active.graphs.Length > 1)
				{
					NNInfo nearest = AstarPath.active.GetNearest(spawnPosition, NNConstraint.Walkable);
					NNInfo nearest2 = AstarPath.active.GetNearest(GameDirector.Instance.Player.CurrentPosition, NNConstraint.Walkable);
					if (PathUtilities.IsPathPossible(nearest.node, nearest2.node))
					{
						continue;
					}
				}
				Bounds bounds2 = bounds;
				bounds2.center += (Vector3)spawnPosition;
				if (!ProCamera2DHelpers.IsWithinCameraBounds(bounds2) && Physics2D.CircleCast(spawnPosition, spawnReferenceRadius, Vector2.zero, 1f, layerMask.value).collider == null)
				{
					return true;
				}
			}
			for (int j = 0; j < 100; j++)
			{
				spawnPosition = ProCamera2DHelpers.GetPointOutsideCamera(unitsFromCamera);
				if (Physics2D.CircleCast(spawnPosition, spawnReferenceRadius, Vector2.zero, 1f, layerMask.value).collider == null)
				{
					return true;
				}
			}
			spawnPosition = new Vector2(100000f, 100000f);
			return false;
		}

		public static bool GetOffScreenSpawnPosition(float spawnReferenceRadius, Bounds bounds, float unitsFromCamera, LayerMask layerMask, int maxNumberOfTries, out Vector2 spawnPosition)
		{
			for (int i = 0; i < maxNumberOfTries; i++)
			{
				spawnPosition = ProCamera2DHelpers.GetPointOutsideCamera(unitsFromCamera, bounds);
				if (AstarPath.active.graphs.Length > 1)
				{
					NNInfo nearest = AstarPath.active.GetNearest(spawnPosition, NNConstraint.Walkable);
					NNInfo nearest2 = AstarPath.active.GetNearest(GameDirector.Instance.Player.CurrentPosition, NNConstraint.Walkable);
					if (PathUtilities.IsPathPossible(nearest.node, nearest2.node))
					{
						continue;
					}
				}
				Bounds bounds2 = bounds;
				bounds2.center += (Vector3)spawnPosition;
				if (!ProCamera2DHelpers.IsWithinCameraBounds(bounds2) && Physics2D.CircleCast(spawnPosition, spawnReferenceRadius, Vector2.zero, 1f, layerMask.value).collider == null)
				{
					return true;
				}
			}
			for (int j = 0; j < maxNumberOfTries; j++)
			{
				spawnPosition = ProCamera2DHelpers.GetPointOutsideCamera(unitsFromCamera);
				if (Physics2D.CircleCast(spawnPosition, spawnReferenceRadius, Vector2.zero, 1f, layerMask.value).collider == null)
				{
					return true;
				}
			}
			spawnPosition = new Vector2(100000f, 100000f);
			return false;
		}

		public static bool GetOffScreenSpawnPositionInDirection(float spawnReferenceRadius, Bounds bounds, float unitsFromCamera, LayerMask layerMask, Vector2 direction, out Vector2 spawnPosition, float angle = 90f, Vector3 playerPosition = default(Vector3))
		{
			for (int i = 0; i < 100; i++)
			{
				Vector2 direction2 = Quaternion.AngleAxis(Random.Range((0f - angle) / 2f, angle / 2f), Vector3.forward) * direction;
				spawnPosition = ProCamera2DHelpers.GetPointOutsideCamera(direction2, unitsFromCamera, bounds);
				AstarPath.active.GetNearest(spawnPosition, NNConstraint.Walkable);
				AstarPath.active.GetNearest(GameDirector.Instance.Player.CurrentPosition, NNConstraint.Walkable);
				if (Physics2D.CircleCast(spawnPosition, spawnReferenceRadius, Vector2.zero, 1f, layerMask.value).collider == null)
				{
					return true;
				}
			}
			for (int j = 0; j < 100; j++)
			{
				spawnPosition = ProCamera2DHelpers.GetPointOutsideCamera(unitsFromCamera);
				if (Physics2D.CircleCast(spawnPosition, spawnReferenceRadius, Vector2.zero, 1f, layerMask.value).collider == null)
				{
					return true;
				}
			}
			spawnPosition = new Vector2(100000f, 100000f);
			return false;
		}

		public static bool GetOffScreenSpawnPositionInDirection(float spawnReferenceRadius, Bounds bounds, float unitsFromCamera, LayerMask layerMask, int maxNumberOfTries, Vector2 direction, out Vector2 spawnPosition)
		{
			for (int i = 0; i < maxNumberOfTries; i++)
			{
				Vector2 direction2 = Quaternion.AngleAxis(Random.Range(0f, 90f), Vector3.forward) * direction;
				spawnPosition = ProCamera2DHelpers.GetPointOutsideCamera(direction2, unitsFromCamera, bounds);
				NNInfo nearest = AstarPath.active.GetNearest(spawnPosition, NNConstraint.Walkable);
				NNInfo nearest2 = AstarPath.active.GetNearest(GameDirector.Instance.Player.CurrentPosition, NNConstraint.Walkable);
				if (!PathUtilities.IsPathPossible(nearest.node, nearest2.node))
				{
					Bounds bounds2 = bounds;
					bounds2.center += (Vector3)spawnPosition;
					if (!ProCamera2DHelpers.IsWithinCameraBounds(bounds2) && Physics2D.CircleCast(spawnPosition, spawnReferenceRadius, Vector2.zero, 1f, layerMask.value).collider == null)
					{
						return true;
					}
				}
			}
			for (int j = 0; j < maxNumberOfTries; j++)
			{
				spawnPosition = ProCamera2DHelpers.GetPointOutsideCamera(unitsFromCamera);
				if (Physics2D.CircleCast(spawnPosition, spawnReferenceRadius, Vector2.zero, 1f, layerMask.value).collider == null)
				{
					return true;
				}
			}
			spawnPosition = new Vector2(100000f, 100000f);
			return false;
		}

		public static bool GetSpawnLocationIsValid(Vector2 spawnPosition, float spawnReferenceRadius, LayerMask layerMask)
		{
			if (Physics2D.CircleCast(spawnPosition, spawnReferenceRadius, Vector2.zero, 1f, layerMask.value).collider == null)
			{
				return true;
			}
			return false;
		}

		public static Vector2 GetValidPositionInDirectionInsideCollider(Vector2 startPoint, Vector2 direction, LayerMask obstacleMask, float distance)
		{
			float radius = 1f;
			RaycastHit2D[] array = new RaycastHit2D[10];
			RaycastHit2D raycastHit2D = Physics2D.CircleCast(startPoint, radius, Vector2.zero, 1f, obstacleMask);
			Physics2D.Raycast(startPoint + direction * distance, -direction, new ContactFilter2D
			{
				useLayerMask = true,
				layerMask = obstacleMask
			}, array, distance + 0.1f);
			RaycastHit2D[] array2 = array;
			for (int i = 0; i < array2.Length; i++)
			{
				RaycastHit2D raycastHit2D2 = array2[i];
				if (raycastHit2D.collider == raycastHit2D2.collider && (bool)raycastHit2D2.collider)
				{
					return raycastHit2D2.point;
				}
			}
			return startPoint;
		}
	}
}
