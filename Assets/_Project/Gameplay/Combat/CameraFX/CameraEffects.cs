using Com.LuisPedroFonseca.ProCamera2D;
using UnityEngine;

namespace AstralShift.HellMaiden.CameraFX
{
	public class CameraEffects : MonoBehaviour
	{
		public static CameraEffects Instance;

		public FullscreenEffect healthEffect;

		public FullscreenEffect poetDeathEffect;

		public FullscreenEffect warningEffect;

		private void Awake()
		{
			if (Instance == null)
			{
				Instance = this;
			}
			else
			{
				Object.Destroy(this);
			}
		}

		public Vector3 WorldToScreenPointNoShake(Vector3 worldPosition)
		{
			Matrix4x4 inverse = Matrix4x4.TRS(ProCamera2D.Instance.LocalPosition, ProCamera2D.Instance.transform.localRotation, Vector3.one).inverse;
			Vector4 vector = ProCamera2D.Instance.GameCamera.projectionMatrix * inverse * new Vector4(worldPosition.x, worldPosition.y, worldPosition.z, 1f);
			Vector3 vector2 = vector / vector.w;
			return new Vector3((vector2.x + 1f) * 0.5f * (float)Screen.width, (vector2.y + 1f) * 0.5f * (float)Screen.height, vector.z);
		}

		public Vector3 ScreenToWorldPointNoShake(Vector3 screenPosition)
		{
			Vector3 vector = new Vector3(screenPosition.x / (float)Screen.width * 2f - 1f, screenPosition.y / (float)Screen.height * 2f - 1f, screenPosition.z);
			Vector4 vector2 = new Vector4(vector.x, vector.y, vector.z * 2f - 1f, 1f);
			Matrix4x4 inverse = Matrix4x4.TRS(ProCamera2D.Instance.LocalPosition, ProCamera2D.Instance.GameCamera.transform.localRotation, Vector3.one).inverse;
			Vector4 vector3 = (ProCamera2D.Instance.GameCamera.projectionMatrix * inverse).inverse * vector2;
			vector3 /= vector3.w;
			return new Vector3(vector3.x, vector3.y, vector3.z);
		}

		public Vector3 ScreenToViewportPointNoShake(Vector3 screenPosition)
		{
			Vector3 vector = new Vector3(screenPosition.x / (float)Screen.width * 2f - 1f, screenPosition.y / (float)Screen.height * 2f - 1f, screenPosition.z);
			Matrix4x4 inverse = Matrix4x4.TRS(ProCamera2D.Instance.LocalPosition, ProCamera2D.Instance.GameCamera.transform.localRotation, Vector3.one).inverse;
			Matrix4x4 projectionMatrix = ProCamera2D.Instance.GameCamera.projectionMatrix;
			Matrix4x4 inverse2 = (projectionMatrix * inverse).inverse;
			Vector4 vector2 = new Vector4(vector.x, vector.y, vector.z * 2f - 1f, 1f);
			Vector4 vector3 = inverse2 * vector2;
			vector3 /= vector3.w;
			Vector3 vector4 = inverse * vector3;
			Vector4 vector5 = projectionMatrix * new Vector4(vector4.x, vector4.y, vector4.z, 1f);
			vector5 /= vector5.w;
			return new Vector3((vector5.x + 1f) * 0.5f, (vector5.y + 1f) * 0.5f, vector4.z);
		}

		public void Shake(CameraShakeSettings settings)
		{
			if (!GameDirector.Instance.Settings.CameraShake || settings == null)
			{
				return;
			}
			switch (settings.mode)
			{
			case CameraShakeSettings.ShakeMode.ShakePreset:
				if (!(settings.shakePreset == null))
				{
					ProCamera2DShake.Instance.Shake(settings.shakePreset);
				}
				break;
			case CameraShakeSettings.ShakeMode.ConstantShakePreset:
				if (!(settings.constantShakePreset == null))
				{
					ProCamera2DShake.Instance.ConstantShake(settings.constantShakePreset);
				}
				break;
			case CameraShakeSettings.ShakeMode.Manual:
				ProCamera2DShake.Instance.Shake(settings.duration, settings.strength, settings.vibrato, settings.randomness, settings.useRandomInitialAngle ? (-1f) : settings.initialAngle, settings.rotation, settings.smoothness, settings.ignoreTimeScale);
				break;
			}
		}

		public void Shake(string presetName)
		{
			if (GameDirector.Instance.Settings.CameraShake)
			{
				ProCamera2DShake.Instance.Shake(presetName);
			}
		}

		public void Shake(int index)
		{
			if (GameDirector.Instance.Settings.CameraShake)
			{
				ProCamera2DShake.Instance.Shake(index);
			}
		}

		public void Shake(CameraShakeSettings settings, float strengthIncrement)
		{
			if (!GameDirector.Instance.Settings.CameraShake || settings == null)
			{
				return;
			}
			switch (settings.mode)
			{
			case CameraShakeSettings.ShakeMode.ShakePreset:
				if (!(settings.shakePreset == null))
				{
					Vector3 vector = settings.shakePreset.Strength.normalized * strengthIncrement;
					ProCamera2DShake.Instance.Shake(settings.duration, settings.shakePreset.Strength + vector, settings.shakePreset.Vibrato, settings.shakePreset.Randomness, settings.shakePreset.UseRandomInitialAngle ? (-1f) : settings.shakePreset.InitialAngle, settings.shakePreset.Rotation, settings.shakePreset.Smoothness, settings.shakePreset.IgnoreTimeScale);
				}
				break;
			case CameraShakeSettings.ShakeMode.ConstantShakePreset:
				if (!(settings.constantShakePreset == null))
				{
					ConstantShakePreset constantShakePreset = ScriptableObject.CreateInstance<ConstantShakePreset>();
					constantShakePreset.Intensity = settings.constantShakePreset.Intensity + strengthIncrement;
					constantShakePreset.Layers = settings.constantShakePreset.Layers;
					ProCamera2DShake.Instance.ConstantShake(settings.constantShakePreset);
					Object.Destroy(constantShakePreset);
				}
				break;
			case CameraShakeSettings.ShakeMode.Manual:
			{
				Vector3 vector = settings.strength.normalized * strengthIncrement;
				ProCamera2DShake.Instance.Shake(settings.duration, settings.strength + vector, settings.vibrato, settings.randomness, settings.useRandomInitialAngle ? (-1f) : settings.initialAngle, settings.rotation, settings.smoothness, settings.ignoreTimeScale);
				break;
			}
			}
		}

		public void ConstantShake(int index)
		{
			if (GameDirector.Instance.Settings.CameraShake)
			{
				ProCamera2DShake.Instance.ConstantShake(index);
			}
		}

		public void ConstantShake(string presetName)
		{
			if (GameDirector.Instance.Settings.CameraShake)
			{
				ProCamera2DShake.Instance.ConstantShake(presetName);
			}
		}

		public void StopShake()
		{
			ProCamera2DShake.Instance.StopConstantShaking();
		}

		public void Health(int value)
		{
			Health();
		}

		public void Health()
		{
			if (!(healthEffect == null))
			{
				healthEffect.Trigger();
			}
		}

		public void HealthConstant(bool state)
		{
			if (!(healthEffect == null))
			{
				if (state)
				{
					healthEffect.Enable();
				}
				else
				{
					healthEffect.Disable();
				}
			}
		}

		public void PoetDeathScreenFlashEFX()
		{
			if (!(poetDeathEffect == null))
			{
				poetDeathEffect.Trigger();
			}
		}

		public void StartWarning()
		{
			if (!(warningEffect == null))
			{
				warningEffect.Enable();
			}
		}

		public void EndWarning()
		{
			if (!(warningEffect == null))
			{
				warningEffect.Disable();
			}
		}
	}
}
