using Animancer;
using AstralShift.HellMaiden.Audio;
using UnityEngine;

namespace AstralShift.HellMaiden.Characters.Effects
{
	public class CharacterFootstepsBehaviour : MonoBehaviour
	{
		[SerializeField]
		private Transform sourceTransform;

		[SerializeField]
		private int[] footstepKeyFrames;

		[SerializeField]
		private CharacterFootstepsAudioHandler footstepsAudioHandler;

		[SerializeField]
		private CharacterFootstepsParticlesHandler footstepsParticlesHandler;

		public void TryCreateEvents(ClipTransition clipTransition)
		{
			if (footstepKeyFrames != null && footstepKeyFrames.Length != 0)
			{
				float num = clipTransition.Clip.length * clipTransition.Clip.frameRate;
				int[] array = footstepKeyFrames;
				for (int i = 0; i < array.Length; i++)
				{
					float normalizedTime = (float)array[i] / num;
					clipTransition.Events.Add(normalizedTime, TriggerFootstepEffects);
				}
			}
		}

		private void TriggerFootstepEffects()
		{
			if ((bool)footstepsAudioHandler)
			{
				footstepsAudioHandler.PlayFootstep(sourceTransform);
			}
			if ((bool)footstepsParticlesHandler)
			{
				footstepsParticlesHandler.PlayParticles(sourceTransform.position);
			}
		}
	}
}
