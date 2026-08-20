using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.Rendering.ParticleSystems
{
	[ExecuteAlways]
	[RequireComponent(typeof(ParticleSystem))]
	public class ParticleSystemRotationSetter : MonoBehaviour
	{
		public float lifetimeThreshold = 0.05f;

		[SerializeField]
		[HideInInspector]
		private ParticleSystem _particleSystem;

		private ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[200];

		private List<int> _indexes;

		private void Reset()
		{
			_particleSystem = GetComponent<ParticleSystem>();
		}

		private void OnEnable()
		{
			ASPreRenderPass.BeforeRenderCallback += UpdateRotation;
		}

		private void OnDisable()
		{
			ASPreRenderPass.BeforeRenderCallback -= UpdateRotation;
		}

		private void UpdateRotation()
		{
			if (_particleSystem == null)
			{
				Reset();
			}
			else
			{
				if (_particleSystem.particleCount == 0)
				{
					return;
				}
				_particleSystem.GetParticles(_particles);
				if (_indexes == null)
				{
					_indexes = new List<int>();
				}
				_indexes.Clear();
				for (int i = 0; i < _particleSystem.particleCount; i++)
				{
					if (_particles[i].startLifetime - _particles[i].remainingLifetime <= lifetimeThreshold)
					{
						_indexes.Add(i);
					}
				}
				for (int j = 0; j < _indexes.Count; j++)
				{
					_particles[_indexes[j]].position = base.transform.position;
					_particles[_indexes[j]].rotation3D = base.transform.rotation.eulerAngles;
				}
				_particleSystem.SetParticles(_particles, _particleSystem.particleCount);
			}
		}
	}
}
