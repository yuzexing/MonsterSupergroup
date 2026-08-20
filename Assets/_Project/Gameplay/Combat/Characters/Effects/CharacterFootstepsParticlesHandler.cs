using System.Collections.Generic;
using AstralShift.Rendering;
using UnityEngine;

namespace AstralShift.HellMaiden.Characters.Effects
{
	public class CharacterFootstepsParticlesHandler : MonoBehaviour
	{
		public List<FootstepsParticles> footstepsParticles = new List<FootstepsParticles>();

		public void PlayParticles(Vector2 position)
		{
			if (!base.enabled)
			{
				return;
			}
			int greyScaleValueFromPosition = MaterialGreyScaleManager.Instance.GetGreyScaleValueFromPosition(position);
			foreach (FootstepsParticles footstepsParticle in footstepsParticles)
			{
				footstepsParticle.PlayParticles(greyScaleValueFromPosition);
			}
		}
	}
}
