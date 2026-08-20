using System;
using AstralShift.Helpers.Attributes;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	[Serializable]
	public class ShaderPropertyProgressionScaler : BaseProgressionScaler
	{
		public Renderer renderer;

		public string propertyName;

		public int materialIndex;

		public ShaderPropertyType valueType;

		[Tooltip("When enabled, scaling is applied to the default value; otherwise, it is applied to the current value.\n \nWARNING: Only set to false if the shader property is being modified by external logic.")]
		public bool useDefaultValue = true;

		[Tooltip("If true, the value is forced to its default whenever the scaling factor is zero.")]
		public bool constraintToDefault = true;

		[ReadOnly]
		public float floatValue;

		[ReadOnly]
		public Vector4 vectorValue;

		[ConditionalHide("useDefaultValue", true)]
		[Tooltip("Default value for the shader property. The scaling factor is applied to this value.")]
		public float defaultFloatValue;

		[Tooltip("Default value for the shader property. The scaling factor is applied to this value.")]
		[ConditionalHide("useDefaultValue", true)]
		public Vector4 defaultVectorValue;

		[Tooltip("Scaling factor for the shader property")]
		public float floatScalingFactor = 1f;

		[Tooltip("Scaling factor for the shader property")]
		public Vector4 vectorScalingFactor = Vector4.one;

		public TillingOffsetScaler tillingOffsetScaler;

		public bool clampMin;

		public bool clampMax;

		[ConditionalHide("clampMin", true)]
		public float floatMinValue;

		[ConditionalHide("clampMin", true)]
		public Vector4 vectorMinValue;

		[ConditionalHide("clampMax", true)]
		public float floatMaxValue;

		[ConditionalHide("clampMax", true)]
		public Vector4 vectorMaxValue;

		public override void Apply(float percentageMultiplier)
		{
			if (!renderer)
			{
				return;
			}
			base.percentageMultiplier = percentageMultiplier;
			int nameID = Shader.PropertyToID(propertyName);
			Material material = (Application.isPlaying ? renderer.materials[materialIndex] : renderer.sharedMaterials[materialIndex]);
			if (!material.HasProperty(nameID))
			{
				return;
			}
			switch (valueType)
			{
			case ShaderPropertyType.Float:
			{
				bool shouldConstraint3 = useDefaultValue && constraintToDefault;
				float num4 = material.GetFloat(nameID);
				float baseAxis = (useDefaultValue ? defaultFloatValue : num4);
				floatValue = ApplyAxisScale(baseAxis, num4, floatScalingFactor, floatMinValue, floatMaxValue, clampMin, clampMax, shouldConstraint3);
				material.SetFloat(nameID, floatValue);
				break;
			}
			case ShaderPropertyType.Vector:
			{
				bool shouldConstraint4 = useDefaultValue && constraintToDefault;
				Vector4 vector3 = material.GetVector(nameID);
				Vector4 vector4 = (useDefaultValue ? defaultVectorValue : vector3);
				float x3 = ApplyAxisScale(vector4.x, vector3.x, vectorScalingFactor.x, vectorMinValue.x, vectorMaxValue.x, clampMin, clampMax, shouldConstraint4);
				float y3 = ApplyAxisScale(vector4.y, vector3.y, vectorScalingFactor.y, vectorMinValue.y, vectorMaxValue.y, clampMin, clampMax, shouldConstraint4);
				float z = ApplyAxisScale(vector4.z, vector3.z, vectorScalingFactor.z, vectorMinValue.z, vectorMaxValue.z, clampMin, clampMax, shouldConstraint4);
				float w = ApplyAxisScale(vector4.w, vector3.w, vectorScalingFactor.w, vectorMinValue.w, vectorMaxValue.w, clampMin, clampMax, shouldConstraint4);
				vectorValue = new Vector4(x3, y3, z, w);
				material.SetVector(nameID, vectorValue);
				break;
			}
			case ShaderPropertyType.TillingAndOffset:
			{
				Vector2 textureScale = material.GetTextureScale(nameID);
				Vector2 vector = (tillingOffsetScaler.useDefaultTillingValue ? tillingOffsetScaler.defaultTillingValue : textureScale);
				if (tillingOffsetScaler.useTransformScaleRatio && (bool)tillingOffsetScaler.transformToFetchScale)
				{
					Vector3 localScale = tillingOffsetScaler.transformToFetchScale.localScale;
					float num = Mathf.Max(localScale.x, 0.0001f);
					float num2 = Mathf.Max(localScale.y, 0.0001f);
					float num3 = Mathf.Max(localScale.z, 0.0001f);
					switch (tillingOffsetScaler.uAxisScale)
					{
					case AxisPriority.X:
						if (tillingOffsetScaler.uInvertScale)
						{
							vector.x /= num;
						}
						else
						{
							vector.x *= num;
						}
						break;
					case AxisPriority.Y:
						if (tillingOffsetScaler.uInvertScale)
						{
							vector.x /= num2;
						}
						else
						{
							vector.x *= num2;
						}
						break;
					case AxisPriority.Z:
						if (tillingOffsetScaler.uInvertScale)
						{
							vector.x /= num3;
						}
						else
						{
							vector.x *= num3;
						}
						break;
					}
					switch (tillingOffsetScaler.vAxisScale)
					{
					case AxisPriority.X:
						if (tillingOffsetScaler.vInvertScale)
						{
							vector.y /= num;
						}
						else
						{
							vector.y *= num;
						}
						break;
					case AxisPriority.Y:
						if (tillingOffsetScaler.vInvertScale)
						{
							vector.y /= num2;
						}
						else
						{
							vector.y *= num2;
						}
						break;
					case AxisPriority.Z:
						if (tillingOffsetScaler.vInvertScale)
						{
							vector.y /= num3;
						}
						else
						{
							vector.y *= num3;
						}
						break;
					}
				}
				bool shouldConstraint = tillingOffsetScaler.useDefaultTillingValue && tillingOffsetScaler.constraintTillingToDefault;
				float x = ApplyAxisScale(vector.x, textureScale.x, tillingOffsetScaler.tillingScalingFactor.x, tillingOffsetScaler.tillingMinValue.x, tillingOffsetScaler.tillingMaxValue.x, tillingOffsetScaler.tillingClampMin, tillingOffsetScaler.tillingClampMax, shouldConstraint);
				float y = ApplyAxisScale(vector.y, textureScale.y, tillingOffsetScaler.tillingScalingFactor.y, tillingOffsetScaler.tillingMinValue.y, tillingOffsetScaler.tillingMaxValue.y, tillingOffsetScaler.tillingClampMin, tillingOffsetScaler.tillingClampMax, shouldConstraint);
				tillingOffsetScaler.tillingValue = new Vector2(x, y);
				material.SetTextureScale(nameID, tillingOffsetScaler.tillingValue);
				if (tillingOffsetScaler.autoOffsetRecenter)
				{
					tillingOffsetScaler.offsetValue = tillingOffsetScaler.defaultOffsetValue + (tillingOffsetScaler.defaultTillingValue - tillingOffsetScaler.tillingValue) * 0.5f;
					material.SetTextureOffset(nameID, tillingOffsetScaler.offsetValue);
					break;
				}
				Vector2 textureOffset = material.GetTextureOffset(nameID);
				Vector2 vector2 = (tillingOffsetScaler.useDefaultOffsetValue ? tillingOffsetScaler.defaultOffsetValue : textureOffset);
				bool shouldConstraint2 = tillingOffsetScaler.useDefaultOffsetValue && tillingOffsetScaler.constraintOffsetToDefault;
				float x2 = ApplyAxisScale(vector2.x, textureOffset.x, tillingOffsetScaler.offsetScalingFactor.x, tillingOffsetScaler.offsetMinValue.x, tillingOffsetScaler.offsetMaxValue.x, tillingOffsetScaler.offsetClampMin, tillingOffsetScaler.offsetClampMax, shouldConstraint2);
				float y2 = ApplyAxisScale(vector2.y, textureOffset.y, tillingOffsetScaler.offsetScalingFactor.y, tillingOffsetScaler.offsetMinValue.y, tillingOffsetScaler.offsetMaxValue.y, tillingOffsetScaler.offsetClampMin, tillingOffsetScaler.offsetClampMax, shouldConstraint2);
				tillingOffsetScaler.offsetValue = new Vector2(x2, y2);
				material.SetTextureOffset(nameID, tillingOffsetScaler.offsetValue);
				break;
			}
			}
		}

		private float ApplyAxisScale(float baseAxis, float currentAxis, float factor, float min, float max, bool useMin, bool useMax, bool shouldConstraint)
		{
			if (!shouldConstraint && factor == 0f)
			{
				return currentAxis;
			}
			float value = baseAxis * (1f + percentageMultiplier * factor);
			float min2 = (useMin ? min : float.MinValue);
			float max2 = (useMax ? max : float.MaxValue);
			return Mathf.Clamp(value, min2, max2);
		}
	}
}
