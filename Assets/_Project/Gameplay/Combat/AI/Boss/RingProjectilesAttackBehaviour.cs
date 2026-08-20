using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Boss.Minos;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Helpers;
using AstralShift.Helpers;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss
{
	public class RingProjectilesAttackBehaviour : BossAttackBehaviour
	{
		public enum RingProjectilesType
		{
			Random = 0,
			Crossed = 1
		}

		[Header("Positioning")]
		[SerializeField]
		protected MinosMovementController movementController;

		[SerializeField]
		protected Transform centerPosition;

		[SerializeField]
		protected Transform[] positions;

		protected int _currentPositionIndex;

		[Header("Settings")]
		[SerializeField]
		protected ProjectileBossAttack attackPrefab;

		[SerializeField]
		protected int numberOfProjectiles;

		[SerializeField]
		protected float distanceOfCamera = 5f;

		[SerializeField]
		protected float offscreenTimeout = 5f;

		[SerializeField]
		protected float maxVelocityTimeout = 5f;

		[SerializeField]
		protected float velocityFromEdgeCompensation = 0.5f;

		[SerializeField]
		protected float speed;

		private Coroutine _movementCoroutine;

		private GenericPooler<ProjectileBossAttack> _pooler;

		private const float ToCenterPositionTolerance = 1f;

		private List<ProjectileBossAttack> attackProjectiles;

		private Vector2[] directions;

		public RingProjectilesType ringProjectilesType;

		public float sub = 1f;

		public float div = 10f;

		[Header("Arc Settings")]
		public float totalAngle = 90f;

		public float radiusX = 5f;

		public float radiusY = 3f;

		public Vector3 center = Vector3.zero;

		private void Awake()
		{
			directions = new Vector2[4]
			{
				Vector2.right + Vector2.up,
				Vector2.left + Vector2.up,
				Vector2.right,
				Vector2.left
			};
		}

		public override void Positioning()
		{
			Vector3 nextPosition = GetNextPosition();
			movementController.StopMovement();
			movementController.SetDestination(nextPosition, onPositioningEnd, moveSpeed, shootBullets ? shooter : null);
			movementController.ResumeMovement();
		}

		private Vector3 GetNextPosition()
		{
			if (Vector2.Distance(movementController.Position, centerPosition.position) < 1f)
			{
				Vector2 normalized = UnityEngine.Random.insideUnitCircle.normalized;
				int num = 0;
				float num2 = -1f;
				for (int i = 0; i < positions.Length; i++)
				{
					Vector2 rhs = (positions[i].position - centerPosition.position).normalized;
					float num3 = Vector2.Dot(normalized, rhs);
					if (num3 > num2)
					{
						num2 = num3;
						num = i;
					}
				}
				_currentPositionIndex = num;
				return positions[num].position;
			}
			_currentPositionIndex++;
			if (_currentPositionIndex == positions.Length)
			{
				_currentPositionIndex = 0;
			}
			return positions[_currentPositionIndex].position;
		}

		public override void Warning()
		{
			movementController.StopMovement();
			BarkWarning();
			WarningBossAnimation(onWarningEnd);
		}

		public override void Attack()
		{
			AttackBossAnimation(null);
			switch (ringProjectilesType)
			{
			case RingProjectilesType.Random:
				LaunchRandomProjectiles();
				break;
			case RingProjectilesType.Crossed:
				LaunchCrossedProjectiles();
				break;
			}
		}

		protected void LaunchRandomProjectiles()
		{
			Vector2 normalized = directions[UnityEngine.Random.Range(0, directions.Length)].normalized;
			LaunchProjectiles(normalized);
		}

		protected void LaunchProjectiles(Vector2 _currentDirection)
		{
			if (_pooler == null)
			{
				_pooler = PoolManager.Instance.GetOrCreatePooler(attackPrefab);
			}
			Vector3[] evenlySpacedArcPoints = GetEvenlySpacedArcPoints(numberOfProjectiles, _currentDirection);
			attackProjectiles = new List<ProjectileBossAttack>();
			for (int i = 0; i < numberOfProjectiles; i++)
			{
				ProjectileBossAttack attack = _pooler.GetOrCreate(null, activate: true);
				attack.transform.position = new Vector3(evenlySpacedArcPoints[i].x, evenlySpacedArcPoints[i].y, 100f);
				attack.RotateToDirection(-_currentDirection);
				int i2 = i;
				Action onEnd = delegate
				{
					int num = Mathf.Min(i2, numberOfProjectiles - 1 - i2);
					float num2 = maxVelocityTimeout - (float)num * (sub - (float)num / div) * velocityFromEdgeCompensation;
					attack.Launch(speed, -_currentDirection, rotateToDirection: true, offscreenTimeout, num2, ReturnToPool);
					attack.RunLoopAnimation();
				};
				attack.RunInAnimation(onEnd);
				attackProjectiles.Add(attack);
				void ReturnToPool()
				{
					_pooler.Return(attack);
				}
			}
			onAttackEnd?.Invoke();
		}

		protected void LaunchCrossedProjectiles()
		{
			LaunchProjectiles(Vector2.right + Vector2.up);
			StartCoroutine(Wait.SetTimeout(0.75f, delegate
			{
				LaunchProjectiles(Vector2.left + Vector2.up);
			}));
		}

		public override void Dispose()
		{
			if (attackProjectiles != null)
			{
				Stop();
				for (int i = 0; i < attackProjectiles.Count; i++)
				{
					UnityEngine.Object.Destroy(attackProjectiles[i].gameObject);
				}
			}
			StopAllCoroutines();
			base.gameObject.SetActive(value: false);
		}

		public override void Stop()
		{
			attackProjectiles?.Clear();
		}

		public static Vector3[] GenerateInLinePositionsOutsideCamera(int positionsCount, Vector2 direction, float distanceToCameraBounds)
		{
			Bounds cameraWorldSpaceBounds = CameraHelpers.GetCameraWorldSpaceBounds();
			Vector3 vector = cameraWorldSpaceBounds.center + (Vector3)direction * (cameraWorldSpaceBounds.extents.magnitude + distanceToCameraBounds);
			Vector2 vector2 = new Vector2(0f - direction.y, direction.x);
			float num = cameraWorldSpaceBounds.size.magnitude / (float)positionsCount;
			Vector3[] array = new Vector3[positionsCount];
			for (int i = 0; i < positionsCount; i++)
			{
				array[i] = vector + (Vector3)vector2 * (((float)i - (float)(positionsCount - 1) / 2f) * num);
			}
			return array;
		}

		public Vector3[] GetEvenlySpacedArcPoints(int numberOfObjects, Vector2 direction)
		{
			float num = Mathf.Atan2(direction.y, direction.x) * 57.29578f;
			int num2 = 100;
			float a = num - totalAngle / 2f;
			float b = num + totalAngle / 2f;
			List<Vector3> list = new List<Vector3>();
			List<float> list2 = new List<float>();
			float num3 = 0f;
			Vector3? vector = null;
			for (int i = 0; i <= num2; i++)
			{
				float t = (float)i / (float)num2;
				float f = Mathf.Lerp(a, b, t) * (MathF.PI / 180f);
				float x = center.x + radiusX * Mathf.Cos(f);
				float y = center.y + radiusY * Mathf.Sin(f);
				Vector3 vector2 = new Vector3(x, y, center.z);
				list.Add(vector2);
				if (vector.HasValue)
				{
					num3 += Vector3.Distance(vector.Value, vector2);
					list2.Add(num3);
				}
				else
				{
					list2.Add(0f);
				}
				vector = vector2;
			}
			List<Vector3> list3 = new List<Vector3>();
			float num4 = num3 / (float)(numberOfObjects - 1);
			int j = 0;
			for (int k = 0; k < numberOfObjects; k++)
			{
				float num5;
				for (num5 = num4 * (float)k; j < list2.Count - 1 && list2[j + 1] < num5; j++)
				{
				}
				float num6 = list2[j];
				float num7 = list2[j + 1];
				Vector3 a2 = list[j];
				Vector3 b2 = list[j + 1];
				float t2 = (num5 - num6) / (num7 - num6);
				Vector3 item = Vector3.Lerp(a2, b2, t2);
				list3.Add(item);
			}
			return list3.ToArray();
		}
	}
}
