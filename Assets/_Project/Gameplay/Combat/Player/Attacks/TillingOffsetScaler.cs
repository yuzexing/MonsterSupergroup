using System;
using AstralShift.Helpers.Attributes;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	[Serializable]
	public class TillingOffsetScaler
	{
		[Tooltip("When enabled, scaling is applied to the default value; otherwise, it is applied to the current value.\n \nWARNING: Only set to false if the tilling is being modified by external logic.")]
		public bool useDefaultTillingValue = true;

		[ConditionalHide("useDefaultTillingValue", true)]
		[Tooltip("If true, the value is forced to its default whenever the scaling factor is zero.")]
		public bool constraintTillingToDefault = true;

		[Tooltip("Default tilling value. The scaling factor is applied to this value.")]
		public Vector2 defaultTillingValue = Vector2.one;

		[ReadOnly]
		public Vector2 tillingValue = Vector2.one;

		[Tooltip("Scaling factor for the shader property")]
		public Vector2 tillingScalingFactor = Vector2.one;

		public bool tillingClampMin;

		public bool tillingClampMax;

		[Tooltip("When enabled, the base tiling value is multiplied by the Transform's local scale. This is useful for maintaining consistent texture density (texel density) as an object scales.")]
		public bool useTransformScaleRatio;

		[ConditionalHide("useTransformScaleRatio", true)]
		public Transform transformToFetchScale;

		[ConditionalHide("useTransformScaleRatio", true)]
		[Tooltip("Determines which axes of the transform scale affect the tiling U axis.")]
		public AxisPriority uAxisScale;

		[ConditionalHide("useTransformScaleRatio", true)]
		[Tooltip("If true, the tiling will decrease as the transform scale increases (stretching the texture). If false, tiling increases with scale (maintaining density).")]
		public bool uInvertScale;

		[ConditionalHide("useTransformScaleRatio", true)]
		[Tooltip("Determines which axes of the transform scale affect the tiling V axis.")]
		public AxisPriority vAxisScale = AxisPriority.Y;

		[ConditionalHide("useTransformScaleRatio", true)]
		[Tooltip("If true, the tiling will decrease as the transform scale increases (stretching the texture). If false, tiling increases with scale (maintaining density).")]
		public bool vInvertScale;

		[Tooltip("When enabled, scaling is applied to the default value; otherwise, it is applied to the current value.\n \nWARNING: Only set to false if the offset is being modified by external logic.")]
		public bool useDefaultOffsetValue = true;

		[ConditionalHide("useDefaultOffsetValue", true)]
		[Tooltip("If true, the value is forced to its default whenever the scaling factor is zero.")]
		public bool constraintOffsetToDefault = true;

		[ConditionalHide("tillingClampMin", true)]
		public Vector2 tillingMinValue;

		[ConditionalHide("tillingClampMax", true)]
		public Vector2 tillingMaxValue;

		public bool autoOffsetRecenter;

		[Tooltip("Default offset value. The scaling factor is applied to this value.")]
		public Vector2 defaultOffsetValue = Vector2.zero;

		[ReadOnly]
		public Vector2 offsetValue = Vector2.zero;

		[Tooltip("Scaling factor for the shader property")]
		public Vector2 offsetScalingFactor = Vector2.one;

		public bool offsetClampMin;

		public bool offsetClampMax;

		[ConditionalHide("offsetClampMin", true)]
		public Vector2 offsetMinValue;

		[ConditionalHide("offsetClampMax", true)]
		public Vector2 offsetMaxValue;
	}
}
