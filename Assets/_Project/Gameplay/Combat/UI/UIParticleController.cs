using Coffee.UIExtensions;
using UnityEngine;

namespace AstralShift.HellMaiden.UI
{
	[RequireComponent(typeof(UIParticle))]
	public class UIParticleController : MonoBehaviour
	{
		[HideInInspector]
		[SerializeField]
		protected UIParticle uiParticle;

		[SerializeField]
		protected int burstCount = 1;

		public void PlayBurst()
		{
			for (int i = 0; i < uiParticle.particles.Count; i++)
			{
				if (!(uiParticle.particles[i] == null))
				{
					uiParticle.particles[i].Emit(burstCount);
				}
			}
		}
	}
}
