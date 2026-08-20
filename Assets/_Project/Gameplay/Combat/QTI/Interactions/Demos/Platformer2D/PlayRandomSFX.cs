using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.QTI.Interactions.Demos.Platformer2D
{
	public class PlayRandomSFX : MonoBehaviour
	{
		public AudioClip[] sfx;

		public bool useAudioSource;

		[ConditionalHide("useAudioSource", true)]
		public AudioSource audioSource;

		[ConditionalHide("useAudioSource", true)]
		public bool playOneShot;

		public void Play()
		{
			int num = Random.Range(0, sfx.Length);
			if (useAudioSource)
			{
				if (playOneShot)
				{
					audioSource.PlayOneShot(sfx[num]);
					return;
				}
				audioSource.Stop();
				audioSource.clip = sfx[num];
				audioSource.Play();
			}
			else
			{
				AudioSource.PlayClipAtPoint(sfx[num], base.transform.position);
			}
		}

		public void Stop()
		{
			audioSource.Stop();
		}
	}
}
