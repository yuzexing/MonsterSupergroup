using System.Collections.Generic;
using AstralShift.Rendering;
using UnityEngine;

namespace AstralShift.HellMaiden.Characters.Effects
{
	public class FootstepsParticles : MonoBehaviour
	{
		[SerializeField]
		private ParticleSystem particleSystem;

		[SerializeField]
		private List<MaterialGreyScaleValueLUT.MaterialValue> values;

		public virtual void PlayParticles(int value)
		{
			Play(particleSystem, value);
		}

		protected void Play(ParticleSystem particles, int value)
		{
			foreach (MaterialGreyScaleValueLUT.MaterialValue value2 in values)
			{
				if (value == (int)value2)
				{
					particles.Play();
					break;
				}
			}
		}
	}
}
