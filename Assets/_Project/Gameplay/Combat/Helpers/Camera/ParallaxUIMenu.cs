using UnityEngine;

namespace AstralShift.Helpers.Camera
{
	public class ParallaxUIMenu : ParallaxUI
	{
		public float lerpSpeed = 0.4f;

		private Vector3 velocity = Vector3.zero;

		private void LateUpdate()
		{
			Vector3 vector = UnityEngine.Camera.main.WorldToScreenPoint(Focus.transform.position) - originalPosition;
			float x = (LockXaxis ? originalPosition.x : (originalPosition.x + vector.x * XAxisEffectStrength * (float)xdirection));
			Vector3 position = Vector3.SmoothDamp(target: new Vector3(x, LockYaxis ? originalPosition.y : (originalPosition.y + vector.y * YAxisEffectStrength * (float)ydirection), base.transform.position.z), current: base.transform.position, currentVelocity: ref velocity, smoothTime: lerpSpeed);
			base.transform.position = position;
		}
	}
}
