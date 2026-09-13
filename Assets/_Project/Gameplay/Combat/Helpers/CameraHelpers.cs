using Com.LuisPedroFonseca.ProCamera2D;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace AstralShift.HellMaiden.Helpers
{
	public static class CameraHelpers
	{
		public static Bounds GetCameraWorldSpaceBounds()
		{
			return GameplayCameraGeometry.ViewBounds(ProCamera2D.Instance.GameCamera);
		}

		public static Bounds GetCameraWorldSpaceBounds(this Camera camera)
		{
			return GameplayCameraGeometry.ViewBounds(camera);
		}
	}
}
