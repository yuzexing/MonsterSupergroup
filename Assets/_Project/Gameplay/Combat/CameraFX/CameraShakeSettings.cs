using System;
using Com.LuisPedroFonseca.ProCamera2D;
using UnityEngine;

namespace AstralShift.HellMaiden.CameraFX
{
	[Serializable]
	public class CameraShakeSettings
	{
		public enum ShakeMode
		{
			ShakePreset = 0,
			ConstantShakePreset = 1,
			Manual = 2
		}

		public ShakeMode mode;

		public ShakePreset shakePreset;

		public ConstantShakePreset constantShakePreset;

		public Vector3 strength = new Vector2(10f, 10f);

		[Range(0.02f, 3f)]
		public float duration = 0.5f;

		[Range(1f, 100f)]
		public int vibrato = 10;

		[Range(0f, 1f)]
		public float randomness = 0.1f;

		[Range(0f, 0.5f)]
		public float smoothness = 0.1f;

		public bool useRandomInitialAngle = true;

		[Range(0f, 360f)]
		public float initialAngle;

		public Vector3 rotation;

		public bool ignoreTimeScale;
	}
}
