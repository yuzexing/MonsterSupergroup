using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks.ProjectileMovement
{
	public class PM_CurvedSinCosWave : PM_Base
	{
		[Header("Spiral / Curved")]
		[SerializeField]
		private float turnSpeed = 1f;

		[SerializeField]
		private float radiusPower = 0.75f;

		[SerializeField]
		private float radiusMultiplier = 1f;

		[Header("Wave")]
		[SerializeField]
		private float waveFrequency = 6f;

		[SerializeField]
		private float waveAmplitude = 0.2f;

		[SerializeField]
		private bool isSin = true;

		[SerializeField]
		private bool startMaxAmp;

		private Vector2 origin;

		private float theta;

		private float scaledTime;

		private float startingPhase;

		private Vector2 lastSpiralPosition;

		private float spiralRotation;

		public override void Init(Vector2 direction, Transform rotationTransform, float speed, float despawnTimeout, Transform originTransform = null)
		{
			origin = base.transform.position;
			float num = Mathf.Atan2(direction.y, direction.x);
			theta = 0f;
			scaledTime = 0f;
			startingPhase = (startMaxAmp ? (MathF.PI / 2f) : 0f);
			spiralRotation = num;
			lastSpiralPosition = origin;
		}

		public override void MovementUpdate(Vector2 direction, Transform rotationTransform, float speed)
		{
			float deltaTime = Time.deltaTime;
			if (!(deltaTime <= 0f))
			{
				if (Time.timeScale > 0f)
				{
					scaledTime += deltaTime;
				}
				float num = speed * deltaTime;
				float num2 = Mathf.Max(0.0001f, radiusMultiplier * turnSpeed);
				float num3 = Mathf.Max(0.0001f, radiusPower);
				float f = Mathf.Max(theta, 0.0001f);
				float num4 = num2 * Mathf.Pow(f, num3);
				float num5 = num2 * num3 * Mathf.Pow(f, num3 - 1f);
				float a = Mathf.Sqrt(num4 * num4 + num5 * num5);
				a = Mathf.Max(a, 0.0001f);
				float num6 = num / a;
				theta += num6;
				float num7 = num2 * Mathf.Pow(theta, num3);
				Vector2 v = new Vector2(Mathf.Cos(theta) * num7, Mathf.Sin(theta) * num7);
				Vector2 vector = Rotate(v, spiralRotation);
				Vector2 vector2 = origin + vector;
				Vector2 vector3 = vector2 - lastSpiralPosition;
				if (vector3.sqrMagnitude < 1E-06f)
				{
					vector3 = Rotate(Vector2.right, spiralRotation);
				}
				vector3.Normalize();
				Vector2 vector4 = new Vector2(0f - vector3.y, vector3.x);
				float num8 = waveAmplitude * (isSin ? Mathf.Sin(scaledTime * waveFrequency + startingPhase) : Mathf.Cos(scaledTime * waveFrequency + startingPhase));
				Vector2 vector5 = vector2 + vector4 * num8;
				Vector2 vector6 = vector5 - (Vector2)base.transform.position;
				base.transform.position = vector5;
				lastSpiralPosition = vector2;
				if ((bool)rotationTransform && vector6.sqrMagnitude > 0.0001f)
				{
					float z = Mathf.Atan2(vector6.y, vector6.x) * 57.29578f;
					rotationTransform.rotation = Quaternion.Euler(0f, 0f, z);
				}
			}
		}

		private Vector2 Rotate(Vector2 v, float radians)
		{
			float num = Mathf.Cos(radians);
			float num2 = Mathf.Sin(radians);
			return new Vector2(v.x * num - v.y * num2, v.x * num2 + v.y * num);
		}
	}
}
