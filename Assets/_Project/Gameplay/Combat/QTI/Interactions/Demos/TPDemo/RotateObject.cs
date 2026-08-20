using UnityEngine;

namespace AstralShift.QTI.Interactions.Demos.TPDemo
{
	public class RotateObject : MonoBehaviour
	{
		public float rotationX;

		public float rotationY = 20f;

		public float rotationZ;

		private void Update()
		{
			base.transform.Rotate(new Vector3(rotationX, rotationY, rotationZ) * Time.deltaTime);
		}
	}
}
