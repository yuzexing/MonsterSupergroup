using Com.LuisPedroFonseca.ProCamera2D;
using UnityEngine;

namespace AstralShift.HellMaiden.Helpers
{
	public static class CameraHelpers
	{
		public static Bounds GetCameraWorldSpaceBounds()
		{
			float orthographicSize = ProCamera2D.Instance.GameCamera.orthographicSize;
			float num = orthographicSize * ProCamera2D.Instance.GameCamera.aspect;
			Vector3 position = ProCamera2D.Instance.GameCamera.transform.position;
			Vector3 size = new Vector3(num * 2f, orthographicSize * 2f, 0f);
			return new Bounds(position, size);
		}

		public static Bounds GetCameraWorldSpaceBounds(this Camera camera)
		{
			float orthographicSize = ProCamera2D.Instance.GameCamera.orthographicSize;
			float num = orthographicSize * ProCamera2D.Instance.GameCamera.aspect;
			Vector3 position = ProCamera2D.Instance.GameCamera.transform.position;
			Vector3 size = new Vector3(num * 2f, orthographicSize * 2f, 0f);
			return new Bounds(position, size);
		}
	}
}
