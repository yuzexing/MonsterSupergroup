using System;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class CircularMovementBehavior : EnemyDefaultMovement
	{
		private Transform player;

		[Header("Radius Settings")]
		[SerializeField]
		private float minRadius = 2f;

		[SerializeField]
		private float maxRadius = 4f;

		[HideInInspector]
		public float radius = 3f;

		[Header("Speed Settings")]
		[SerializeField]
		private float minAngularSpeed = 120f;

		[SerializeField]
		private float maxAngularSpeed = 240f;

		private float angularSpeed = 180f;

		private bool rotateClockwise;

		public float radialCorrection = 5f;

		private void Start()
		{
			player = GameDirector.Instance.Player.transform;
			radius = UnityEngine.Random.Range(minRadius, maxRadius);
			angularSpeed = UnityEngine.Random.Range(minAngularSpeed, maxAngularSpeed);
			rotateClockwise = UnityEngine.Random.value > 0.5f;
		}

		public override void MovementUpdate()
		{
			if (!(_transform == null) && _canMove && !(player == null))
			{
				Vector2 vector = _transform.position - player.position;
				float magnitude = vector.magnitude;
				if (magnitude != 0f)
				{
					Vector2 normalized = vector.normalized;
					Vector2 vector2 = new Vector2(0f - normalized.y, normalized.x);
					float num = (rotateClockwise ? 1f : (-1f));
					float num2 = angularSpeed * (MathF.PI / 180f) * radius;
					Vector2 vector3 = vector2 * num2 * num;
					float num3 = magnitude - radius;
					Vector2 vector4 = -normalized * num3 * radialCorrection;
					_rigidbody.linearVelocity = vector3 + vector4;
				}
			}
		}
	}
}
