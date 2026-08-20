using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks.ProjectileMovement
{
	public class PM_SinCosWave : PM_Base
	{
		private float scaledTime;

		public float waveFrequency = 2f;

		public float waveAmplitude = 0.05f;

		[Tooltip("If false: uses Cos.")]
		public bool isSin = true;

		public bool startMaxAmp;

		private float startingAmplitude;

		public override void Init(Vector2 direction, Transform rotationTransform, float speed, float despawnTimeout, Transform originTransform = null)
		{
			scaledTime = 0f;
			if (startMaxAmp)
			{
				startingAmplitude = MathF.PI / 2f;
			}
			else
			{
				startingAmplitude = MathF.PI / 4f;
			}
		}

		public override void MovementUpdate(Vector2 direction, Transform rotationTransform, float speed)
		{
			if (Time.timeScale > 0f)
			{
				scaledTime += Time.deltaTime;
			}
			direction = direction.normalized;
			float num = waveAmplitude * (isSin ? Mathf.Sin(scaledTime * waveFrequency + startingAmplitude) : Mathf.Cos(scaledTime * waveFrequency + startingAmplitude));
			Vector2 vector = new Vector2(0f - direction.y, direction.x);
			Vector2 vector2 = direction * (speed * Time.smoothDeltaTime) + vector * (num * Time.deltaTime);
			base.transform.position += (Vector3)vector2;
			if ((bool)rotationTransform && vector2.sqrMagnitude > 0.0001f)
			{
				float z = Mathf.Atan2(vector2.y, vector2.x) * 57.29578f;
				rotationTransform.rotation = Quaternion.Euler(0f, 0f, z);
			}
		}
	}
}
