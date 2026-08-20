using System;
using UnityEngine;

namespace AstralShift.Helpers
{
	[Serializable]
	public class PityRandom
	{
		[Header("Pity Settings")]
		[Tooltip("The base probability of success (0.0 to 1.0).")]
		[SerializeField]
		[Range(0f, 1f)]
		private float _baseChance = 0.1f;

		[Tooltip("The amount added to the probability after each failure (0.0 to 1.0).")]
		[SerializeField]
		[Range(0f, 1f)]
		private float _pityIncrement = 0.05f;

		[Tooltip("The maximum number of failures allowed before a guaranteed success. Set to 0 to disable hard pity.")]
		[SerializeField]
		[Min(0f)]
		private int _hardPityThreshold;

		private int _failures;

		public float BaseChance => _baseChance;

		public int CurrentFailures => _failures;

		public float CurrentChance
		{
			get
			{
				if (_hardPityThreshold > 0 && _failures >= _hardPityThreshold)
				{
					return 1f;
				}
				return Mathf.Clamp01(_baseChance + (float)_failures * _pityIncrement);
			}
		}

		public PityRandom(float baseChance, float pityIncrement, int hardPityThreshold = 0)
		{
			_baseChance = Mathf.Clamp01(baseChance);
			_pityIncrement = Mathf.Clamp01(pityIncrement);
			_hardPityThreshold = Mathf.Max(0, hardPityThreshold);
			_failures = 0;
		}

		public bool Evaluate()
		{
			if (UnityEngine.Random.value <= CurrentChance)
			{
				Reset();
				return true;
			}
			_failures++;
			return false;
		}

		public void Reset()
		{
			_failures = 0;
		}

		public void AddFailure()
		{
			_failures++;
		}

		public void SetFailures(int failures)
		{
			if (failures < 0)
			{
				failures = 0;
			}
			_failures = failures;
		}
	}
}
