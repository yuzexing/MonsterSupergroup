using Com.LuisPedroFonseca.ProCamera2D;
using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using UnityEngine;

namespace AstralShift.Helpers
{
	public class ProCamera2DHelpers
	{
		public static float DefaultCameraSize = 12f;

		public static Vector2 GetCameraExtents()
		{
			float orthographicSize = ProCamera2D.Instance.GameCamera.orthographicSize;
			return new Vector2(orthographicSize * ProCamera2D.Instance.GameCamera.aspect, orthographicSize);
		}

		public static bool IsWithinCameraBounds(Vector2 position, float extentsMultiplier = 1f)
		{
			if (!ProCamera2D.Exists)
			{
				return false;
			}
			Vector2 cameraExtents = GetCameraExtents();
			cameraExtents *= extentsMultiplier;
			Vector2 vector = ProCamera2D.Instance.GameCamera.transform.position;
			return new Bounds(size: new Vector2(cameraExtents.x * 2f, cameraExtents.y * 2f), center: vector).Contains(position);
		}

		public static bool IsWithinCameraBounds(Bounds bounds, float extentsMultiplier = 1f)
		{
			Vector2 cameraExtents = GetCameraExtents();
			cameraExtents *= extentsMultiplier;
			if (ProCamera2D.Instance == null)
			{
				return false;
			}
			Vector2 vector = ProCamera2D.Instance.GameCamera.transform.position;
			Bounds bounds2 = new Bounds(size: new Vector2(cameraExtents.x * 2f, cameraExtents.y * 2f), center: vector);
			bounds.center = new Vector3(bounds.center.x, bounds.center.y, 0f);
			bounds.size = new Vector3(bounds.size.x, bounds.size.y, 0f);
			if (!bounds2.Contains(bounds.min))
			{
				return bounds2.Contains(bounds.max);
			}
			return true;
		}

		public static Vector2 GetPointOutsideCamera(float distance)
		{
			Vector2 vector = GetCameraExtents() + Vector2.one * distance;
			Vector3 position = ProCamera2D.Instance.GameCamera.transform.position;
			Vector2 normalized = Random.insideUnitCircle.normalized;
			float num = Mathf.Abs(normalized.x);
			float num2 = Mathf.Abs(normalized.y);
			if (num > num2)
			{
				num = ((normalized.x >= 0f) ? vector.x : (0f - vector.x));
				num2 = normalized.y * vector.y;
			}
			else
			{
				num = normalized.x * vector.x;
				num2 = ((normalized.y >= 0f) ? vector.y : (0f - vector.y));
			}
			return new Vector2(position.x + num, position.y + num2);
		}

		public static float GetDistanceToCameraExtentsNonAlloc(Vector2 position)
		{
			if (ProCamera2D.Instance == null)
			{
				return float.PositiveInfinity;
			}
			Vector2 vector = ProCamera2D.Instance.GameCamera.transform.position;
			Vector2 cameraExtents = GetCameraExtents();
			float a = Mathf.Abs(position.x - vector.x) - cameraExtents.x;
			float a2 = Mathf.Abs(position.y - vector.y) - cameraExtents.y;
			float num = Mathf.Max(a, 0f);
			a2 = Mathf.Max(a2, 0f);
			return Mathf.Sqrt(num * num + a2 * a2);
		}

		public static float GetDistanceToCameraExtents(Vector2 position)
		{
			if (IsWithinCameraBounds(position))
			{
				return 0f;
			}
			Vector2 cameraExtents = GetCameraExtents();
			Vector3 position2 = ProCamera2D.Instance.GameCamera.transform.position;
			float num = position2.x - cameraExtents.x;
			float num2 = position2.x + cameraExtents.x;
			float num3 = position2.y - cameraExtents.y;
			float num4 = position2.y + cameraExtents.y;
			float num5 = Mathf.Max(num - position.x, 0f, position.x - num2);
			float num6 = Mathf.Max(num3 - position.y, 0f, position.y - num4);
			return Mathf.Sqrt(num5 * num5 + num6 * num6);
		}

		public static Vector2 GetPointOutsideCamera(Vector2 direction, float distance, Bounds exclusionBounds)
		{
			Vector2 cameraExtents = GetCameraExtents();
			Vector2 vector = new Vector2(exclusionBounds.size.x, exclusionBounds.size.y) * 0.5f;
			Vector2 vector2 = cameraExtents + vector + Vector2.one * distance;
			Vector3 position = ProCamera2D.Instance.GameCamera.transform.position;
			Vector2 normalized = direction.normalized;
			float num;
			float num2;
			if (Mathf.Abs(normalized.x) > Mathf.Abs(normalized.y))
			{
				num = ((normalized.x >= 0f) ? vector2.x : (0f - vector2.x));
				num2 = normalized.y * vector2.y;
			}
			else
			{
				num = normalized.x * vector2.x;
				num2 = ((normalized.y >= 0f) ? vector2.y : (0f - vector2.y));
			}
			return new Vector2(position.x + num, position.y + num2);
		}

		public static Vector2 GetPointOutsideCameraByPlayer(Vector2 direction, float extraDistance, Bounds exclusionBounds, Vector3 centerPoint)
		{
			Vector2 cameraExtents = GetCameraExtents();
			Vector2 vector = new Vector2(exclusionBounds.size.x, exclusionBounds.size.y) * 0.5f;
			Vector2 vector2 = cameraExtents + vector + Vector2.one * extraDistance;
			Vector2 normalized = direction.normalized;
			float a = ((Mathf.Abs(normalized.x) < 0.0001f) ? float.MaxValue : (vector2.x / Mathf.Abs(normalized.x)));
			float b = ((Mathf.Abs(normalized.y) < 0.0001f) ? float.MaxValue : (vector2.y / Mathf.Abs(normalized.y)));
			float num = Mathf.Min(a, b);
			return (Vector2)centerPoint + normalized * num;
		}

		public static Vector2 GetPointOutsideCamera(float distance, Bounds exclusionBounds)
		{
			Vector2 cameraExtents = GetCameraExtents();
			Vector2 vector = new Vector2(exclusionBounds.size.x, exclusionBounds.size.y) * 0.5f;
			Vector2 vector2 = cameraExtents + vector + Vector2.one * distance;
			Vector3 position = ProCamera2D.Instance.GameCamera.transform.position;
			Vector2 normalized = Random.insideUnitCircle.normalized;
			float num;
			float num2;
			if (Mathf.Abs(normalized.x) > Mathf.Abs(normalized.y))
			{
				num = ((normalized.x >= 0f) ? vector2.x : (0f - vector2.x));
				num2 = normalized.y * vector2.y;
			}
			else
			{
				num = normalized.x * vector2.x;
				num2 = ((normalized.y >= 0f) ? vector2.y : (0f - vector2.y));
			}
			return new Vector2(position.x + num, position.y + num2);
		}

		public static void Zoom(float cameraSize, float duration, CustomAnimationCurve animationCurve = null)
		{
			float newCameraSize = ProCamera2D.Instance.GameCamera.orthographicSize;
			TweenerCore<float, float, FloatOptions> t = DOTween.To(() => newCameraSize, delegate(float orthographicSize)
			{
				ProCamera2D.Instance.GameCamera.orthographicSize = orthographicSize;
			}, cameraSize, duration);
			t.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			if (animationCurve != null)
			{
				t.SetEase(animationCurve.GetEaseFunction());
			}
			t.Play();
		}

		public static void ResetZoom(float duration, CustomAnimationCurve animationCurve = null)
		{
			float newCameraSize = ProCamera2D.Instance.GameCamera.orthographicSize;
			TweenerCore<float, float, FloatOptions> t = DOTween.To(() => newCameraSize, delegate(float orthographicSize)
			{
				ProCamera2D.Instance.GameCamera.orthographicSize = orthographicSize;
			}, DefaultCameraSize, duration);
			t.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			if (animationCurve != null)
			{
				t.SetEase(animationCurve.GetEaseFunction());
			}
			t.Play();
		}
	}
}
