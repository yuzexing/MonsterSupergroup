using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.UI
{
	public class UIRectScaleAdjuster : UIBehaviour
	{
		private enum ScaleMode
		{
			Scale = 0,
			Size = 1
		}

		[SerializeField]
		private ScaleMode scaleMode;

		[SerializeField]
		private Vector3 referenceScale = Vector3.one;

		[SerializeField]
		private Vector2 referenceSize;

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
		private Vector2 clamp = new Vector2(0.5f, 1f);

		[Tooltip("Clamps the final scale factor on X axis to a min and max value.")]
		[SerializeField]
		private Vector2 clampXAxis = new Vector2(0.5f, 1f);

		[Tooltip("Clamps the final scale factor on Y axis to a min and max value.")]
		[SerializeField]
		private Vector2 clampYAxis = new Vector2(0.5f, 1f);

		[Tooltip("Clamps the final scale factor on Z axis to a min and max value.")]
		[SerializeField]
		private Vector2 clampZAxis = new Vector2(0.5f, 1f);

		private RectTransform _rectTransform;

		public RectTransform RectTransform
		{
			get
			{
				if (!_rectTransform)
				{
					_rectTransform = GetComponent<RectTransform>();
				}
				return _rectTransform;
			}
		}

		protected override void OnCanvasGroupChanged()
		{
			base.OnCanvasGroupChanged();
			AdjustScale();
		}

		protected override void OnRectTransformDimensionsChange()
		{
			base.OnRectTransformDimensionsChange();
			AdjustScale();
		}

		protected override void OnCanvasHierarchyChanged()
		{
			base.OnCanvasHierarchyChanged();
			AdjustScale();
		}

		public void AdjustScale()
		{
			switch (scaleMode)
			{
			case ScaleMode.Scale:
			{
				if (uniformScale)
				{
					float aspectRatioScaleFactor4 = GetAspectRatioScaleFactor(widthToHeightBlend);
					aspectRatioScaleFactor4 = Mathf.Clamp(aspectRatioScaleFactor4, clamp.x, clamp.y);
					RectTransform.localScale = referenceScale * aspectRatioScaleFactor4;
					break;
				}
				float aspectRatioScaleFactor5 = GetAspectRatioScaleFactor(widthToHeightBlendXAxis);
				float aspectRatioScaleFactor6 = GetAspectRatioScaleFactor(widthToHeightBlendYAxis);
				float aspectRatioScaleFactor7 = GetAspectRatioScaleFactor(widthToHeightBlendZAxis);
				aspectRatioScaleFactor5 = Mathf.Clamp(aspectRatioScaleFactor5, clampXAxis.x, clampXAxis.y);
				aspectRatioScaleFactor6 = Mathf.Clamp(aspectRatioScaleFactor6, clampYAxis.x, clampYAxis.y);
				aspectRatioScaleFactor7 = Mathf.Clamp(aspectRatioScaleFactor7, clampZAxis.x, clampZAxis.y);
				RectTransform.localScale = new Vector3(referenceScale.x * aspectRatioScaleFactor5, referenceScale.y * aspectRatioScaleFactor6, referenceScale.z * aspectRatioScaleFactor7);
				break;
			}
			case ScaleMode.Size:
				if (uniformScale)
				{
					float aspectRatioScaleFactor = GetAspectRatioScaleFactor(widthToHeightBlend);
					aspectRatioScaleFactor = Mathf.Clamp(aspectRatioScaleFactor, clamp.x, clamp.y);
					RectTransform.sizeDelta = referenceSize * aspectRatioScaleFactor;
				}
				else
				{
					float aspectRatioScaleFactor2 = GetAspectRatioScaleFactor(widthToHeightBlendXAxis);
					float aspectRatioScaleFactor3 = GetAspectRatioScaleFactor(widthToHeightBlendYAxis);
					aspectRatioScaleFactor2 = Mathf.Clamp(aspectRatioScaleFactor2, clampXAxis.x, clampXAxis.y);
					aspectRatioScaleFactor3 = Mathf.Clamp(aspectRatioScaleFactor3, clampYAxis.x, clampYAxis.y);
					RectTransform.sizeDelta = new Vector3(referenceSize.x * aspectRatioScaleFactor2, referenceSize.y * aspectRatioScaleFactor3);
				}
				break;
			}
		}

		public static float GetAspectRatioScaleFactor(float blend)
		{
			float num = (float)Screen.width / (float)Screen.height;
			float a = num / 1.7777778f;
			float b = 1.7777778f / num;
			return Mathf.Lerp(a, b, blend);
		}
	}
}
