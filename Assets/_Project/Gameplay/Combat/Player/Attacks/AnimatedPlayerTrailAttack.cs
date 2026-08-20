using System;
using System.Collections.Generic;
using AstralShift.DebugTools;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class AnimatedPlayerTrailAttack : AnimatedAttack
	{
		[Serializable]
		private struct DashDistanceProgressionScaler
		{
			public List<TransformProgressionScaler> transforms;

			public List<ParticleSystemProgressionScaler> particleSystemProperties;

			public List<ShaderPropertyProgressionScaler> shaderProperties;

			public List<CustomProgressionScaler> customScalers;

			private DashDistanceProgressionScaler(bool _)
			{
				transforms = new List<TransformProgressionScaler>();
				particleSystemProperties = new List<ParticleSystemProgressionScaler>();
				shaderProperties = new List<ShaderPropertyProgressionScaler>();
				customScalers = new List<CustomProgressionScaler>();
			}

			public void Apply(float percentageMultiplier)
			{
				if (transforms != null && transforms.Count > 0)
				{
					for (int i = 0; i < transforms.Count; i++)
					{
						transforms[i].Apply(percentageMultiplier);
					}
				}
				if (particleSystemProperties != null && particleSystemProperties.Count > 0)
				{
					for (int j = 0; j < particleSystemProperties.Count; j++)
					{
						particleSystemProperties[j].Apply(percentageMultiplier);
					}
				}
				if (shaderProperties != null && shaderProperties.Count > 0)
				{
					for (int k = 0; k < shaderProperties.Count; k++)
					{
						shaderProperties[k].Apply(percentageMultiplier);
					}
				}
				if (customScalers != null && customScalers.Count > 0)
				{
					for (int l = 0; l < customScalers.Count; l++)
					{
						customScalers[l].Apply(percentageMultiplier);
					}
				}
			}
		}

		[Space(20f)]
		[SerializeField]
		private float referenceDistance = 4f;

		[SerializeField]
		private Vector2 positionOffset;

		[SerializeField]
		private float angleOffset = 180f;

		[SerializeField]
		private bool preApplyDashProgressionScaler = true;

		[SerializeField]
		private DashDistanceProgressionScaler dashDistanceProgressionScaler;

		private PlayerMovement _playerMovement;

		private Vector2 _startPoint;

		private Vector2 _endPoint;

		private const float IsoYFactor = 1.41f;

		public override void Init(WeaponBehaviour behaviour, Action onStart = null, Action onEnd = null)
		{
			_behaviour = behaviour;
			_onStart = onStart;
			_onEnd = onEnd;
			if ((bool)hitbox)
			{
				hitbox.Init(OnHit);
			}
			else
			{
				DBL.Log(DBL.Module.PlayerAttacks, "No Hitbox found: make sure this attack doesn't need it!", 1);
			}
			_playerMovement = GameDirector.Instance.Player;
			_startPoint = _playerMovement.CurrentPosition;
			float dashDistance = _playerMovement.DashDistance;
			Vector2 normalized = _playerMovement.DashDirection.normalized;
			Vector2 vector = normalized * dashDistance;
			vector.y *= 1.41f;
			_endPoint = _startPoint + vector;
			float totalDashTime = _playerMovement.TotalDashTime;
			SetPositionAndRotation(_startPoint + positionOffset, normalized);
			UpdateAnimationLength(totalDashTime);
			if (preApplyDashProgressionScaler)
			{
				ApplyDashProgressionScaler(vector.magnitude);
				if ((bool)progressionScaler)
				{
					progressionScaler.Apply(behaviour);
				}
			}
			else
			{
				if ((bool)progressionScaler)
				{
					progressionScaler.Apply(behaviour);
				}
				ApplyDashProgressionScaler(vector.magnitude);
			}
			_playerMovement = GameDirector.Instance.Player;
		}

		protected override void OnHit(IDamageable damageable)
		{
			if ((bool)hitEffectResolver)
			{
				hitEffectResolver.Initialize(_behaviour);
			}
			if (base.HitEffectMode != DamageMode.None && base.HitEffectMode != DamageMode.ExplosionHit)
			{
				Vector2 nearestPositionInTrail = GetNearestPositionInTrail(damageable.GetPosition());
				_behaviour.OnHit(nearestPositionInTrail, damageable);
			}
		}

		private Vector2 GetNearestPositionInTrail(Vector2 position)
		{
			Vector2 vector = _endPoint - _startPoint;
			float sqrMagnitude = vector.sqrMagnitude;
			if (sqrMagnitude == 0f)
			{
				return base.transform.position;
			}
			float value = Vector2.Dot(position - _startPoint, vector) / sqrMagnitude;
			value = Mathf.Clamp01(value);
			return _startPoint + value * vector;
		}

		private void SetPositionAndRotation(Vector2 position, Vector2 direction)
		{
			base.transform.position = new Vector3(position.x, position.y, base.transform.position.z);
			UpdateRotation(direction);
		}

		public override void UpdateRotation(Vector2 direction)
		{
			direction.y *= 1.41f;
			float num = Mathf.Atan2(direction.x, direction.y) * 57.29578f;
			Quaternion localRotation = Quaternion.Euler(0f, num + angleOffset, 0f);
			if ((bool)rotationTransform)
			{
				rotationTransform.localRotation = localRotation;
			}
			else
			{
				base.transform.localRotation = localRotation;
			}
		}

		private void UpdateAnimationLength(float dashTime)
		{
			float speed = attackStartAnim.Clip.length / dashTime;
			attackStartAnim.Speed = speed;
			base._attackAnimDuration = _behaviour.DurationValue;
		}

		private void ApplyDashProgressionScaler(float distance)
		{
			float percentageMultiplier = distance / referenceDistance - 1f;
			dashDistanceProgressionScaler.Apply(percentageMultiplier);
		}
	}
}
