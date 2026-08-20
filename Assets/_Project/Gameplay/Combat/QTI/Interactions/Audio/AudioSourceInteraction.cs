using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions.Audio
{
	[AddComponentMenu("QTI/Interactions/Audio/AudioSourceInteraction")]
	public class AudioSourceInteraction : Interaction
	{
		public enum MusicAction
		{
			Play = 0,
			Pause = 1,
			Resume = 2,
			Stop = 3
		}

		[Tooltip("AudioSource to affect")]
		public AudioSource audioSource;

		[Tooltip("Action to be performed")]
		public MusicAction action;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			switch (action)
			{
			case MusicAction.Play:
				audioSource.Play();
				break;
			case MusicAction.Pause:
				audioSource.Pause();
				break;
			case MusicAction.Resume:
				audioSource.UnPause();
				break;
			case MusicAction.Stop:
				audioSource.Stop();
				break;
			}
			OnEnd();
		}
	}
}
