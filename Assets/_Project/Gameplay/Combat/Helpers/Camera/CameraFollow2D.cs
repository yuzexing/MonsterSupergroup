using UnityEngine;

namespace AstralShift.Helpers.Camera
{
	public class CameraFollow2D : MonoBehaviour
	{
		public Transform target;

		public float smoothTime = 0.25f;

		public Vector3 offset;

		private Vector3 velocity = Vector3.zero;

		private void LateUpdate()
		{
			if (!(target == null))
			{
				Vector3 vector = target.position + offset;
				vector.z = base.transform.position.z;
				base.transform.position = Vector3.SmoothDamp(base.transform.position, vector, ref velocity, smoothTime);
			}
		}
	}
}
