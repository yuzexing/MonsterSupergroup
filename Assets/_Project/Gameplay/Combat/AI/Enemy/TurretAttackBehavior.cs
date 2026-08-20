using System;
using AstralShift.HellMaiden.Helpers;
using AstralShift.Helpers;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class TurretAttackBehavior : ProjectileConsecutiveAttacks
	{
		[SerializeField]
		private LayerMask obstaclesLayerMask;

		[SerializeField]
		private BoxCollider2D boxCollider;

		private bool spawnAnimationPlayed;

		public float minTeleportDistance = 5f;

		public float maxTeleportDistance = 10f;

		public float minDistanceFromCameraEdge = 5f;

		public float minDistanceFromPlayer = 5f;

		public bool useCameraBoundsToTeleport;

		private Vector3 nextTeleportPosition;

		private void Start()
		{
			boxCollider.enabled = true;
			base.controller.StateMachine.AddAnyTransition(base.controller.Warning);
			if (TeleportToPlayerRadius())
			{
				base.transform.position = nextTeleportPosition;
				base.controller.TransitionToWarning();
			}
		}

		public override void AttackWarningEnter()
		{
			if (!spawnAnimationPlayed)
			{
				base.AttackWarningEnter();
				spawnAnimationPlayed = true;
			}
			else
			{
				base.controller.TransitionToAttacking();
			}
		}

		public override void AttackEnter()
		{
			base.AttackEnter();
			boxCollider.enabled = true;
		}

		public override void RecoveryExit()
		{
			base.RecoveryExit();
			boxCollider.enabled = false;
		}

		private void OnDisable()
		{
			spawnAnimationPlayed = false;
		}

		private bool TeleportToPlayerRadius()
		{
			Vector3 position = base.Target.transform.position;
			if (!useCameraBoundsToTeleport)
			{
				for (int i = 0; i < 100; i++)
				{
					float f = UnityEngine.Random.Range(0f, MathF.PI * 2f);
					float num = UnityEngine.Random.Range(minTeleportDistance, maxTeleportDistance);
					Vector3 vector = new Vector3(Mathf.Cos(f), Mathf.Sin(f), 0f) * num;
					Vector2 vector2 = position + vector;
					if (SpawnHelpers.GetSpawnLocationIsValid(vector2, enemyController.spawnReferenceRadius, obstaclesLayerMask) && ProCamera2DHelpers.IsWithinCameraBounds(vector2))
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
