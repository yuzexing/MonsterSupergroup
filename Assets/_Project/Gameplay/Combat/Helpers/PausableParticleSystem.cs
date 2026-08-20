using System.Collections.Generic;
using AstralShift.Managers;
using UnityEngine;

namespace AstralShift.HellMaiden.Helpers
{
	public class PausableParticleSystem : MonoBehaviour, IPausable
	{
		[SerializeField]
		private List<ParticleSystem> particleReferenceList = new List<ParticleSystem>();

		private void Awake()
		{
			particleReferenceList.Clear();
			particleReferenceList.AddRange(GetComponentsInChildren<ParticleSystem>());
		}

		public void OnPausePausables()
		{
			particleReferenceList.ForEach(delegate(ParticleSystem p)
			{
				p.Pause();
			});
		}

		public void OnResumePausables()
		{
			particleReferenceList.ForEach(delegate(ParticleSystem p)
			{
				p.Play();
			});
		}
	}
}
