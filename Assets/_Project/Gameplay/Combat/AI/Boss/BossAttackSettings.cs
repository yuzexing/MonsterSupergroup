using System;
using AstralShift.Helpers.Attributes;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss
{
	[Serializable]
	public class BossAttackSettings
	{
		public BossAttackBehaviour attack;

		[SerializeField]
		[Range(0f, 1f)]
		protected float baseWeight = 1f;

		[ReadOnly]
		[SerializeField]
		protected float currentWeight = 1f;

		[SerializeField]
		protected float recoveryRate = 0.1f;

		[SerializeField]
		protected float weightReductionFactor = 0.3f;

		[SerializeField]
		protected float minWeightThreshold = 0.1f;

		[SerializeField]
		protected bool hasCooldown;

		[ConditionalHide("hasCooldown", true)]
		[SerializeField]
		protected float cooldownInterval;

		protected float _cooldownTimestamp;

		[ConditionalHide("hasCooldown", true)]
		[SerializeField]
		[ReadOnly]
		protected float elapsedCooldown;

		public float BaseWeight => baseWeight;

		public float CurrentWeight => currentWeight;

		public float RecoveryRate => recoveryRate;

		public float WeightReductionFactor => weightReductionFactor;

		public float MinWeightThreshold => minWeightThreshold;

		public bool HasCooldown => hasCooldown;

		public float CooldownInterval => cooldownInterval;

		public float CooldownTimestamp => cooldownInterval;

		public void ResetWeight()
		{
			SetWeight(BaseWeight);
		}

		public void SetWeight(float value)
		{
			currentWeight = value;
		}

		public void IncreaseWeight(float value)
		{
			currentWeight += value;
		}

		public void DecreaseWeight(float value)
		{
			currentWeight -= value;
		}

		public void ApplyReductionFactor()
		{
			currentWeight *= weightReductionFactor;
		}

		public void SetCooldownTimestamp()
		{
			_cooldownTimestamp = Time.time;
		}

		public void SetElapsedCooldown(float value)
		{
			elapsedCooldown = value;
		}
	}
}
