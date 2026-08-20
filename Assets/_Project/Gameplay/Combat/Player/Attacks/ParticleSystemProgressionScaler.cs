using System;
using System.Reflection;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	[Serializable]
	public class ParticleSystemProgressionScaler : BaseProgressionScaler
	{
		public ParticleSystem particleSystem;

		public ParticleSystemModule moduleType;

		public PropertyGetMode propertyGetMode;

		public MainModuleProperties mainModuleProperties;

		public EmissionModuleProperties emissionModuleProperties;

		public ShapeModuleProperties shapeModuleProperties;

		public VelocityOverLifetimeModuleProperties velocityOverLifetimeModuleProperties;

		public LimitVelocityOverLifetimeModuleProperties limitVelocityOverLifetimeModuleProperties;

		public string propertyName;

		public PropertyType valueType;

		public int intValue;

		public float floatValue;

		public bool boolValue;

		public ParticleSystem.MinMaxCurve minMaxValue;

		public Vector3 vector3Value;

		public int defaultIntValue;

		public float defaultFloatValue;

		public bool defaultBoolValue;

		public ParticleSystem.MinMaxCurve defaultMinMaxValue;

		public Vector3 defaultVector3Value;

		public float floatScalingFactor = 1f;

		public Vector3 vector3ScalingFactor = Vector3.one;

		public bool clampMin;

		public bool clampMax;

		public int intMinValue;

		public float floatMinValue;

		public ParticleSystem.MinMaxCurve minMaxCurveMinValue;

		public Vector3 vector3MinValue;

		public int intMaxValue;

		public float floatMaxValue;

		public ParticleSystem.MinMaxCurve minMaxCurveMaxValue;

		public Vector3 vector3MaxValue;

		public object Value
		{
			get
			{
				switch (valueType)
				{
				case PropertyType.Float:
				{
					float value5 = defaultFloatValue * (1f + percentageMultiplier * floatScalingFactor);
					value5 = Mathf.Clamp(value5, clampMin ? floatMinValue : float.NegativeInfinity, clampMax ? floatMaxValue : float.PositiveInfinity);
					floatValue = value5;
					return floatValue;
				}
				case PropertyType.MinMaxCurve:
				{
					ParticleSystem.MinMaxCurve minMaxCurve = defaultMinMaxValue;
					minMaxCurve.constant *= 1f + percentageMultiplier * floatScalingFactor;
					minMaxCurve.constant = Mathf.Clamp(minMaxCurve.constant, clampMin ? minMaxCurveMinValue.constant : float.NegativeInfinity, clampMax ? minMaxCurveMaxValue.constant : float.PositiveInfinity);
					minMaxCurve.constantMin *= 1f + percentageMultiplier * floatScalingFactor;
					minMaxCurve.constantMin = Mathf.Clamp(minMaxCurve.constantMin, clampMin ? minMaxCurveMinValue.constantMin : float.NegativeInfinity, clampMax ? minMaxCurveMaxValue.constantMin : float.PositiveInfinity);
					minMaxCurve.constantMax *= 1f + percentageMultiplier * floatScalingFactor;
					minMaxCurve.constantMax = Mathf.Clamp(minMaxCurve.constantMax, clampMin ? minMaxCurveMinValue.constantMax : float.NegativeInfinity, clampMax ? minMaxCurveMaxValue.constantMax : float.PositiveInfinity);
					minMaxCurve.curveMultiplier *= 1f + percentageMultiplier * floatScalingFactor;
					minMaxCurve.curveMultiplier = Mathf.Clamp(minMaxCurve.curveMultiplier, clampMin ? minMaxCurveMinValue.curveMultiplier : float.NegativeInfinity, clampMax ? minMaxCurveMaxValue.curveMultiplier : float.PositiveInfinity);
					minMaxValue = minMaxCurve;
					return minMaxValue;
				}
				case PropertyType.Int:
				{
					int value4 = (int)((float)defaultIntValue * (1f + percentageMultiplier * floatScalingFactor));
					value4 = Mathf.Clamp(value4, clampMin ? intMinValue : int.MinValue, clampMax ? intMaxValue : int.MaxValue);
					intValue = value4;
					return intValue;
				}
				case PropertyType.Bool:
				{
					bool flag = defaultBoolValue;
					boolValue = flag;
					return boolValue;
				}
				case PropertyType.Vector3:
				{
					float value = defaultVector3Value.x * (1f + percentageMultiplier * vector3ScalingFactor.x);
					value = Mathf.Clamp(value, clampMin ? vector3MinValue.x : float.NegativeInfinity, clampMax ? vector3MaxValue.x : float.PositiveInfinity);
					float value2 = defaultVector3Value.y * (1f + percentageMultiplier * vector3ScalingFactor.y);
					value2 = Mathf.Clamp(value2, clampMin ? vector3MinValue.y : float.NegativeInfinity, clampMax ? vector3MaxValue.y : float.PositiveInfinity);
					float value3 = defaultVector3Value.z * (1f + percentageMultiplier * vector3ScalingFactor.z);
					value3 = Mathf.Clamp(value3, clampMin ? vector3MinValue.z : float.NegativeInfinity, clampMax ? vector3MaxValue.z : float.PositiveInfinity);
					Vector3 vector = new Vector3(value, value2, value3);
					vector3Value = vector;
					return vector3Value;
				}
				default:
					return null;
				}
			}
		}

		public object DefaultValue
		{
			get
			{
				return valueType switch
				{
					PropertyType.Float => defaultFloatValue, 
					PropertyType.MinMaxCurve => defaultMinMaxValue, 
					PropertyType.Int => defaultIntValue, 
					PropertyType.Bool => defaultBoolValue, 
					PropertyType.Vector3 => defaultVector3Value, 
					_ => null, 
				};
			}
			set
			{
				switch (valueType)
				{
				case PropertyType.Float:
					defaultFloatValue = (float)value;
					break;
				case PropertyType.MinMaxCurve:
					defaultMinMaxValue = (ParticleSystem.MinMaxCurve)value;
					break;
				case PropertyType.Int:
					defaultIntValue = (int)value;
					break;
				case PropertyType.Bool:
					defaultBoolValue = (bool)value;
					break;
				case PropertyType.Vector3:
					defaultVector3Value = (Vector3)value;
					break;
				}
			}
		}

		public override void Apply(float percentageMultiplier)
		{
			if (particleSystem == null)
			{
				return;
			}
			base.percentageMultiplier = percentageMultiplier;
			object obj = moduleType switch
			{
				ParticleSystemModule.Main => particleSystem.main, 
				ParticleSystemModule.Emission => particleSystem.emission, 
				ParticleSystemModule.Shape => particleSystem.shape, 
				ParticleSystemModule.VelocityOverLifetime => particleSystem.velocityOverLifetime, 
				ParticleSystemModule.LimitVelocityOverLifetime => particleSystem.limitVelocityOverLifetime, 
				ParticleSystemModule.LifetimeByEmitterSpeed => particleSystem.lifetimeByEmitterSpeed, 
				ParticleSystemModule.ForceOverLifetime => particleSystem.forceOverLifetime, 
				ParticleSystemModule.SizeOverLifetime => particleSystem.sizeOverLifetime, 
				ParticleSystemModule.SizeBySpeed => particleSystem.sizeBySpeed, 
				ParticleSystemModule.RotationOverLifetime => particleSystem.rotationOverLifetime, 
				ParticleSystemModule.RotationBySpeed => particleSystem.rotationBySpeed, 
				_ => null, 
			};
			if (obj == null)
			{
				return;
			}
			string empty = string.Empty;
			empty = ((propertyGetMode != PropertyGetMode.Auto) ? propertyName : GetPropertyNameFromEnum());
			PropertyInfo property = obj.GetType().GetProperty(empty);
			if (!(property != null) || !property.CanWrite)
			{
				return;
			}
			try
			{
				property.SetValue(obj, Value);
			}
			catch (Exception)
			{
			}
		}

		private string GetPropertyNameFromEnum()
		{
			return moduleType switch
			{
				ParticleSystemModule.Main => mainModuleProperties.ToString(), 
				ParticleSystemModule.Emission => emissionModuleProperties.ToString(), 
				ParticleSystemModule.Shape => shapeModuleProperties.ToString(), 
				ParticleSystemModule.VelocityOverLifetime => velocityOverLifetimeModuleProperties.ToString(), 
				ParticleSystemModule.LimitVelocityOverLifetime => limitVelocityOverLifetimeModuleProperties.ToString(), 
				_ => null, 
			};
		}
	}
}
