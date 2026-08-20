using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class LineRendererProgressionScaler : CustomProgressionScaler
	{
		public LineRenderer lineRenderer;

		[Header("Width Settings")]
		public float defaultWidth = 1f;

		public float widthScalingFactor = 1f;

		public float currentWidth;

		public bool clampWidthMin;

		public float widthMin;

		public bool clampWidthMax;

		public float widthMax;

		private float _percentageMultiplier;

		public override void Apply(float percentageMultiplier)
		{
			if (!(lineRenderer == null))
			{
				_percentageMultiplier = percentageMultiplier;
				float value = defaultWidth + _percentageMultiplier * widthScalingFactor;
				value = Mathf.Clamp(value, clampWidthMin ? widthMin : float.NegativeInfinity, clampWidthMax ? widthMax : float.PositiveInfinity);
				currentWidth = value;
				lineRenderer.widthMultiplier = currentWidth;
			}
		}

		public override void SetDefaults()
		{
		}
	}
}
