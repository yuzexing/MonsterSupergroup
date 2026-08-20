using System.Collections.Generic;
using AstralShift.HellMaiden.Combat;
using AstralShift.Helpers.Attributes;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class ParticleSystemShapeFillerProgressionScaler : CustomProgressionScaler
	{
		public enum Shape
		{
			Circle = 0
		}

		public enum FillMode
		{
			Lines = 0
		}

		public enum FillDirection
		{
			ZAxis = 0
		}

		public ParticleSystem particleSystem;

		[SerializeField]
		private bool _poolPrefab;

		private GenericPooler<ParticleSystem> _pooler;

		[SerializeField]
		[ReadOnly]
		private List<ParticleSystem> _particleSystems = new List<ParticleSystem>();

		[SerializeField]
		private Shape shapeMode;

		[SerializeField]
		private FillMode fillMode;

		[SerializeField]
		private FillDirection fillDirection;

		public float defaultRadius;

		[ReadOnly]
		public float currentRadius;

		public float lineSpacing = 1f;

		[SerializeField]
		private bool scaleEmission;

		[SerializeField]
		[ReadOnly]
		private float _defaultEmissionValue;

		[SerializeField]
		[ReadOnly]
		private float _currentEmissionValue;

		public override void Apply(float percentageMultiplier)
		{
			percentageMultiplier = Mathf.Clamp(percentageMultiplier, 0f, float.PositiveInfinity);
			if (!(particleSystem == null) && shapeMode == Shape.Circle)
			{
				ApplyCircle(percentageMultiplier);
			}
		}

		private void ApplyCircle(float percentageMultiplier)
		{
			currentRadius = defaultRadius * (1f + percentageMultiplier);
			_defaultEmissionValue = particleSystem.emission.rateOverTime.constant;
			if (_poolPrefab && _pooler == null)
			{
				_pooler = PoolManager.Instance.GetOrCreatePooler(particleSystem);
			}
			if (fillMode != FillMode.Lines)
			{
				return;
			}
			int num = Mathf.FloorToInt(2f * currentRadius / lineSpacing);
			if (_particleSystems == null)
			{
				_particleSystems = new List<ParticleSystem>();
			}
			if (_particleSystems.Count > num)
			{
				int num2 = _particleSystems.Count - num;
				for (int i = 0; i < num2; i++)
				{
					if (_poolPrefab)
					{
						GenericPooler<ParticleSystem> pooler = _pooler;
						List<ParticleSystem> particleSystems = _particleSystems;
						pooler.Return(particleSystems[particleSystems.Count - 1]);
					}
					else
					{
						List<ParticleSystem> particleSystems2 = _particleSystems;
						Object.Destroy(particleSystems2[particleSystems2.Count - 1].gameObject);
					}
					_particleSystems.RemoveAt(_particleSystems.Count - 1);
				}
			}
			if (_particleSystems.Count < num)
			{
				int num3 = num - _particleSystems.Count;
				for (int j = 0; j < num3; j++)
				{
					if (_poolPrefab)
					{
						_particleSystems.Add(_pooler.GetOrCreate(base.transform, activate: true));
					}
					else
					{
						_particleSystems.Add(Object.Instantiate(particleSystem, base.transform));
					}
				}
			}
			for (int num4 = _particleSystems.Count - 1; num4 >= 0; num4--)
			{
				float num5 = 0f;
				if (fillDirection == FillDirection.ZAxis)
				{
					num5 = 0f - currentRadius + (float)num4 * lineSpacing;
					_particleSystems[num4].transform.localPosition = new Vector3(0f, 0f, num5);
				}
				float num6 = Mathf.Sqrt(currentRadius * currentRadius - num5 * num5);
				if (num6 == 0f)
				{
					if (_poolPrefab)
					{
						_pooler.Return(_particleSystems[num4]);
					}
					else
					{
						Object.Destroy(_particleSystems[num4].gameObject);
					}
					_particleSystems.RemoveAt(num4);
					break;
				}
				_particleSystems[num4].transform.localScale = new Vector3(num6 * 2f, _particleSystems[num4].transform.localScale.y, _particleSystems[num4].transform.localScale.z);
				if (scaleEmission)
				{
					ParticleSystem.EmissionModule emission = _particleSystems[num4].emission;
					_currentEmissionValue = _defaultEmissionValue * (1f + percentageMultiplier);
					emission.rateOverTime = new ParticleSystem.MinMaxCurve(_currentEmissionValue);
				}
			}
		}

		public override void SetDefaults()
		{
			_defaultEmissionValue = particleSystem.emission.rateOverTime.constant;
			_currentEmissionValue = _defaultEmissionValue;
			currentRadius = defaultRadius;
		}
	}
}
