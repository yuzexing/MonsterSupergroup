using System;
using AstralShift.Helpers.Attributes;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	[Serializable]
	public class TransformProgressionScaler : BaseProgressionScaler
	{
		public Transform transform;

		[Tooltip("When enabled, scaling is applied to the default value; otherwise, it is applied to the current value.\n \nWARNING: Only set to false if the transform scale is being modified by external logic.")]
		public bool useDefaultValue = true;

		[ConditionalHide("useDefaultValue", true)]
		[Tooltip("If true, the scale is forced to its default value whenever the scaling factor for a given axis is zero.")]
		public bool constraintToDefault = true;

		[ConditionalHide("useDefaultValue", true)]
		public Vector3 defaultValue = Vector3.one;

		public Vector3 scallingFactor = Vector3.one;

		[Tooltip("If true, the scaling factor is inverted (divided). This is useful for scaling down.")]
		public bool invertScale;

		[ReadOnly]
		public Vector3 value = Vector3.one;

		public bool clampMin;

		[ConditionalHide("clampMin", true)]
		public Vector3 valueMin;

		public bool clampMax;

		[ConditionalHide("clampMax", true)]
		public Vector3 valueMax;

		public override void Apply(float percentageMultiplier)
		{
			if ((bool)transform)
			{
				base.percentageMultiplier = percentageMultiplier;
				Vector3 defaultOrCurrentScaleValue = GetDefaultOrCurrentScaleValue();
				Vector3 localScale = transform.localScale;
				float x = ApplyAxisScale(defaultOrCurrentScaleValue.x, localScale.x, scallingFactor.x, valueMin.x, valueMax.x);
				float y = ApplyAxisScale(defaultOrCurrentScaleValue.y, localScale.y, scallingFactor.y, valueMin.y, valueMax.y);
				float z = ApplyAxisScale(defaultOrCurrentScaleValue.z, localScale.z, scallingFactor.z, valueMin.z, valueMax.z);
				value = new Vector3(x, y, z);
				transform.localScale = value;
			}
		}

		private float ApplyAxisScale(float baseAxis, float currentAxis, float factor, float min, float max)
		{
			if ((!useDefaultValue || !constraintToDefault) && factor == 0f)
			{
				return currentAxis;
			}
			float num = 1f + percentageMultiplier * factor;
			float num2 = ((invertScale && num != 0f) ? (baseAxis / num) : (baseAxis * num));
			float min2 = (clampMin ? min : float.NegativeInfinity);
			float max2 = (clampMax ? max : float.PositiveInfinity);
			return Mathf.Clamp(num2, min2, max2);
		}

		private Vector3 GetDefaultOrCurrentScaleValue()
		{
			if (!useDefaultValue)
			{
				return transform.localScale;
			}
			return defaultValue;
		}
	}
}
