using UnityEngine;

namespace AstralShift.Helpers.Camera
{
	public class ParallaxUI : Parallax
	{
		private void LateUpdate()
		{
			Vector3 vector = UnityEngine.Camera.main.WorldToScreenPoint(Focus.transform.position) - originalPosition;
			float x = (LockXaxis ? originalPosition.x : (originalPosition.x + vector.x * XAxisEffectStrength * (float)xdirection));
			float y = (LockYaxis ? originalPosition.y : (originalPosition.y + vector.y * YAxisEffectStrength * (float)ydirection));
			base.transform.position = new Vector3(x, y, base.transform.position.z);
		}
	}
}
