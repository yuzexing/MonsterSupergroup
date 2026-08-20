using UnityEngine;

namespace AstralShift.Helpers.Camera
{
	public class AspectRatioScaleAdjuster : MonoBehaviour
	{
		[SerializeField]
		private Vector3 referenceScale = Vector3.one;

		[SerializeField]
		private bool uniformScale = true;

		[Tooltip("0 = Full Width Height, 1 = Full Height Weight")]
		[Range(0f, 1f)]
		[SerializeField]
		private float widthToHeightBlend = 0.5f;

		[Tooltip("0 = Full Width Height, 1 = Full Height Weight")]
		[Range(0f, 1f)]
		[SerializeField]
		private float widthToHeightBlendXAxis = 0.5f;

		[Tooltip("0 = Full Width Height, 1 = Full Height Weight")]
		[Range(0f, 1f)]
		[SerializeField]
		private float widthToHeightBlendYAxis = 0.5f;

		[Tooltip("0 = Full Width Height, 1 = Full Height Weight")]
		[Range(0f, 1f)]
		[SerializeField]
		private float widthToHeightBlendZAxis = 0.5f;

		[Tooltip("Clamps the final scale factor to a min and max value.")]
		[SerializeField]
		private Vector2 scaleClamp = new Vector2(0.5f, 1f);

		[Tooltip("Clamps the final scale factor on X axis to a min and max value.")]
		[SerializeField]
		private Vector2 scaleClampXAxis = new Vector2(0.5f, 1f);

		[Tooltip("Clamps the final scale factor on Y axis to a min and max value.")]
		[SerializeField]
		private Vector2 scaleClampYAxis = new Vector2(0.5f, 1f);

		[Tooltip("Clamps the final scale factor on Z axis to a min and max value.")]
		[SerializeField]
		private Vector2 scaleClampZAxis = new Vector2(0.5f, 1f);

		[Space]
		[SerializeField]
		private Vector3 referenceOffset = Vector3.zero;

		[SerializeField]
		private Vector3 offsetIncrement = Vector3.zero;

		[SerializeField]
		private float offsetMultiplier = 1f;

		[Tooltip("0 = Full Width Height, 1 = Full Height Weight")]
		[Range(0f, 1f)]
		[SerializeField]
		private float widthToHeightOffsetBlend = 0.5f;

		[Tooltip("Clamps the final offset factor on X Axis to a min and max value.")]
		[SerializeField]
		private Vector2 offsetXClamp = new Vector2(0.5f, 1f);

		[Tooltip("Clamps the final offset factor on Y Axis to a min and max value.")]
		[SerializeField]
		private Vector2 offsetYClamp = new Vector2(0.5f, 1f);

		[Tooltip("Clamps the final offset factor on Z Axis to a min and max value.")]
		[SerializeField]
		private Vector2 offsetZClamp = new Vector2(0.5f, 1f);

		private Transform _transform;

		public Transform Transform
		{
			get
			{
				if (!_transform)
				{
					_transform = base.transform;
				}
				return _transform;
			}
		}

		public void Start()
		{
			Adjust();
		}

		public void Adjust()
		{
			if (uniformScale)
			{
				float aspectRatioScaleFactor = GetAspectRatioScaleFactor(widthToHeightBlend);
				aspectRatioScaleFactor = Mathf.Clamp(aspectRatioScaleFactor, scaleClamp.x, scaleClamp.y);
				Transform.localScale = referenceScale * aspectRatioScaleFactor;
			}
			else
			{
				float aspectRatioScaleFactor2 = GetAspectRatioScaleFactor(widthToHeightBlendXAxis);
				float aspectRatioScaleFactor3 = GetAspectRatioScaleFactor(widthToHeightBlendYAxis);
				float aspectRatioScaleFactor4 = GetAspectRatioScaleFactor(widthToHeightBlendZAxis);
				aspectRatioScaleFactor2 = Mathf.Clamp(aspectRatioScaleFactor2, scaleClampXAxis.x, scaleClampXAxis.y);
				aspectRatioScaleFactor3 = Mathf.Clamp(aspectRatioScaleFactor3, scaleClampYAxis.x, scaleClampYAxis.y);
				aspectRatioScaleFactor4 = Mathf.Clamp(aspectRatioScaleFactor4, scaleClampZAxis.x, scaleClampZAxis.y);
				Transform.localScale = new Vector3(referenceScale.x * aspectRatioScaleFactor2, referenceScale.y * aspectRatioScaleFactor3, referenceScale.z * aspectRatioScaleFactor4);
			}
			float aspectRatioOffsetFactor = GetAspectRatioOffsetFactor(widthToHeightOffsetBlend);
			Vector3 localPosition = referenceOffset + offsetIncrement * aspectRatioOffsetFactor;
			localPosition.x = Mathf.Clamp(localPosition.x, offsetXClamp.x, offsetXClamp.y);
			localPosition.y = Mathf.Clamp(localPosition.y, offsetYClamp.x, offsetYClamp.y);
			localPosition.z = Mathf.Clamp(localPosition.z, offsetZClamp.x, offsetZClamp.y);
			Transform.localPosition = localPosition;
		}

		public float GetAspectRatioScaleFactor(float blend)
		{
			float num = (float)Screen.width / (float)Screen.height;
			float a = num / 1.7777778f;
			float b = 1.7777778f / num;
			return Mathf.Lerp(a, b, blend);
		}

		public float GetAspectRatioOffsetFactor(float blend)
		{
			float num = (float)Screen.width / (float)Screen.height;
			float a = num / 1.7777778f;
			float b = 1.7777778f / num;
			return (Mathf.Lerp(a, b, blend) - 1f) * offsetMultiplier;
		}
	}
}
