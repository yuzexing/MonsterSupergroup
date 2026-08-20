using UnityEngine;

namespace AstralShift.Helpers
{
	public static class UIResolutionHelpers
	{
		public static float GetResAdjustedScreenSpaceOffset(float offset)
		{
			float num = Mathf.Min((float)Screen.width / 1920f, (float)Screen.height / 1080f);
			return offset * num;
		}

		public static Vector3 GetResAdjustedScreenSpacePosition(Vector3 position)
		{
			float num = Mathf.Min((float)Screen.width / 1920f, (float)Screen.height / 1080f);
			return position * num;
		}

		public static float GetAspectRatioScaleFactor()
		{
			float num = 1.7777778f;
			return (float)Screen.width / (float)Screen.height / num;
		}
	}
}
