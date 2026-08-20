using AstralShift.Helpers.Attributes;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class PositionProgressionScaler : CustomProgressionScaler
	{
		public new Transform transform;

		public Vector3 defaultValue = Vector3.one;

		public Vector3 scallingFactor = Vector3.one;

		[ReadOnly]
		public Vector3 value = Vector3.one;

		public bool clampMin;

		[ConditionalHide("clampMin", true)]
		public Vector3 valueMin;

		public bool clampMax;

		[ConditionalHide("clampMax", true)]
		public Vector3 valueMax;

		private float percentageMultiplier;

		public override void Apply(float percentageMultiplier)
		{
			if (!(transform == null))
			{
				this.percentageMultiplier = percentageMultiplier;
				float num = defaultValue.x + this.percentageMultiplier * scallingFactor.x;
				num = Mathf.Clamp(num, clampMin ? valueMin.x : float.NegativeInfinity, clampMax ? valueMax.x : float.PositiveInfinity);
				float num2 = defaultValue.y + this.percentageMultiplier * scallingFactor.y;
				num2 = Mathf.Clamp(num2, clampMin ? valueMin.y : float.NegativeInfinity, clampMax ? valueMax.y : float.PositiveInfinity);
				float num3 = defaultValue.z + this.percentageMultiplier * scallingFactor.z;
				num3 = Mathf.Clamp(num3, clampMin ? valueMin.z : float.NegativeInfinity, clampMax ? valueMax.z : float.PositiveInfinity);
				value = new Vector3(num, num2, num3);
				transform.localPosition = value;
			}
		}

		public override void SetDefaults()
		{
			transform.localPosition = defaultValue;
		}
	}
}
