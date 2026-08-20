using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class AttackProgressionScaler : MonoBehaviour
	{
		public List<TransformProgressionScaler> damageTransforms = new List<TransformProgressionScaler>();

		public List<ParticleSystemProgressionScaler> damageParticleSystemProperties = new List<ParticleSystemProgressionScaler>();

		public List<ShaderPropertyProgressionScaler> damageShaderProperties = new List<ShaderPropertyProgressionScaler>();

		public List<CustomProgressionScaler> damageCustomScalers = new List<CustomProgressionScaler>();

		public bool allowNegativeDamageMultiplier;

		public List<TransformProgressionScaler> speedTransforms = new List<TransformProgressionScaler>();

		public List<ParticleSystemProgressionScaler> speedParticleSystemProperties = new List<ParticleSystemProgressionScaler>();

		public List<ShaderPropertyProgressionScaler> speedShaderProperties = new List<ShaderPropertyProgressionScaler>();

		public List<CustomProgressionScaler> speedCustomScalers = new List<CustomProgressionScaler>();

		public bool allowNegativeSpeedMultiplier;

		public List<TransformProgressionScaler> sizeTransforms = new List<TransformProgressionScaler>();

		public List<ParticleSystemProgressionScaler> sizeParticleSystemProperties = new List<ParticleSystemProgressionScaler>();

		public List<ShaderPropertyProgressionScaler> sizeShaderProperties = new List<ShaderPropertyProgressionScaler>();

		public List<CustomProgressionScaler> sizeCustomScalers = new List<CustomProgressionScaler>();

		public bool allowNegativeSizeMultiplier;

		public List<TransformProgressionScaler> durationTransforms = new List<TransformProgressionScaler>();

		public List<ParticleSystemProgressionScaler> durationParticleSystemProperties = new List<ParticleSystemProgressionScaler>();

		public List<ShaderPropertyProgressionScaler> durationShaderProperties = new List<ShaderPropertyProgressionScaler>();

		public List<CustomProgressionScaler> durationCustomScalers = new List<CustomProgressionScaler>();

		public bool allowNegativeDurationMultiplier;

		public List<TransformProgressionScaler> projectileCountTransforms = new List<TransformProgressionScaler>();

		public List<ParticleSystemProgressionScaler> projectileCountParticleSystemProperties = new List<ParticleSystemProgressionScaler>();

		public List<ShaderPropertyProgressionScaler> projectileCountShaderProperties = new List<ShaderPropertyProgressionScaler>();

		public List<CustomProgressionScaler> projectileCountCustomScalers = new List<CustomProgressionScaler>();

		private float _damagePercentageMultiplier;

		private float _speedPercentageMultiplier;

		private float _sizePercentageMultiplier;

		private float _durationPercentageMultiplier;

		private float _projectileCountPercentageMultiplier;

		public void Apply(WeaponBehaviour behaviour)
		{
			ApplyDamage(behaviour.DamageMultiplierSum);
			ApplySpeed(behaviour.SpeedMultiplierSum);
			ApplySize(behaviour.SizeMultiplierSum);
			ApplyDuration(behaviour.DurationMultiplierSum);
			ApplyProjectileCount(behaviour.ProjectileCountValue, behaviour.BaseAttackStats.projectileCount);
		}

		public void ApplyDamage(float percentageMultiplier)
		{
			if (!allowNegativeDamageMultiplier)
			{
				percentageMultiplier = Mathf.Max(0f, percentageMultiplier);
			}
			_damagePercentageMultiplier = percentageMultiplier;
			ApplyToList(damageTransforms, _damagePercentageMultiplier);
			ApplyToList(damageParticleSystemProperties, _damagePercentageMultiplier);
			ApplyToList(damageShaderProperties, _damagePercentageMultiplier);
			ApplyToList(damageCustomScalers, _damagePercentageMultiplier);
		}

		public void ApplySpeed(float percentageMultiplier)
		{
			if (!allowNegativeSpeedMultiplier)
			{
				percentageMultiplier = Mathf.Max(0f, percentageMultiplier);
			}
			_speedPercentageMultiplier = percentageMultiplier;
			ApplyToList(speedTransforms, _speedPercentageMultiplier);
			ApplyToList(speedParticleSystemProperties, _speedPercentageMultiplier);
			ApplyToList(speedShaderProperties, _speedPercentageMultiplier);
			ApplyToList(speedCustomScalers, _speedPercentageMultiplier);
		}

		public void ApplySize(float percentageMultiplier)
		{
			if (!allowNegativeSizeMultiplier)
			{
				percentageMultiplier = Mathf.Max(0f, percentageMultiplier);
			}
			_sizePercentageMultiplier = percentageMultiplier;
			ApplyToList(sizeTransforms, _sizePercentageMultiplier);
			ApplyToList(sizeParticleSystemProperties, _sizePercentageMultiplier);
			ApplyToList(sizeShaderProperties, _sizePercentageMultiplier);
			ApplyToList(sizeCustomScalers, _sizePercentageMultiplier);
		}

		public void ApplyDuration(float percentageMultiplier)
		{
			if (!allowNegativeDurationMultiplier)
			{
				percentageMultiplier = Mathf.Max(0f, percentageMultiplier);
			}
			_durationPercentageMultiplier = percentageMultiplier;
			ApplyToList(durationTransforms, _durationPercentageMultiplier);
			ApplyToList(durationParticleSystemProperties, _durationPercentageMultiplier);
			ApplyToList(durationShaderProperties, _durationPercentageMultiplier);
			ApplyToList(durationCustomScalers, _durationPercentageMultiplier);
		}

		public void ApplyProjectileCountMultiplier(float percentageMultiplier)
		{
			_projectileCountPercentageMultiplier = Mathf.Max(0f, percentageMultiplier);
			ApplyToList(projectileCountTransforms, _projectileCountPercentageMultiplier);
			ApplyToList(projectileCountParticleSystemProperties, _projectileCountPercentageMultiplier);
			ApplyToList(projectileCountShaderProperties, _projectileCountPercentageMultiplier);
			ApplyToList(projectileCountCustomScalers, _projectileCountPercentageMultiplier);
		}

		private void ApplyProjectileCount(int currentProjectileCount, int baseProjectileCount)
		{
			float percentageMultiplier = ((baseProjectileCount > 0) ? ((float)currentProjectileCount / (float)baseProjectileCount) : 0f);
			ApplyProjectileCountMultiplier(percentageMultiplier);
		}

		public void SetDefaultValues()
		{
			SetDamageDefaults();
			SetSpeedDefaults();
			SetSizeDefaults();
			SetDurationDefaults();
			SetProjectileCountDefaults();
		}

		public void SetDamageDefaults()
		{
			ApplyDamage(0f);
		}

		public void SetSpeedDefaults()
		{
			ApplySpeed(0f);
		}

		public void SetSizeDefaults()
		{
			ApplySize(0f);
		}

		public void SetDurationDefaults()
		{
			ApplyDuration(0f);
		}

		public void SetProjectileCountDefaults()
		{
			ApplyProjectileCountMultiplier(0f);
		}

		private void ApplyToList<T>(List<T> scalers, float multiplier) where T : IProgressionScaler
		{
			if (scalers != null && scalers.Count != 0)
			{
				for (int i = 0; i < scalers.Count; i++)
				{
					scalers[i].Apply(multiplier);
				}
			}
		}
	}
}
