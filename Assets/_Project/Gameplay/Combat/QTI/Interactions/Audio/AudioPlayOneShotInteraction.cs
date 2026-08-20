using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions.Audio
{
	[AddComponentMenu("QTI/Interactions/Audio/AudioPlayOneShotInteraction")]
	public class AudioPlayOneShotInteraction : Interaction
	{
		public enum AudioPlayOneShotInteractionMode
		{
			AudioSource = 0,
			Position2D = 1,
			Position3D = 2
		}

		[Tooltip("USE AUDIO SOURCE: uses supplied audio source to play the clip.\nPOSITION2D: Creates a one time use Audio Source to play the audio clip at AudioListener position.\nPOSITION3D: Creates a one time use Audio Source to play the audio clip at clip position.")]
		public AudioPlayOneShotInteractionMode mode;

		[Tooltip("Audio Clip to be played")]
		public AudioClip audioClip;

		public float volume = 1f;

		[Tooltip("Audio Source to use to play the clip, if no Audio Source is present, one will be created at clip position")]
		public AudioSource audioSource;

		[Tooltip("World space position to play the clip, only used if no Audio Source is provided, otherwise will use Audio Source Position")]
		public Transform clipPosition;

		private AudioListener _audioListener;

		private void Awake()
		{
			if (mode == AudioPlayOneShotInteractionMode.Position2D)
			{
				_audioListener = Object.FindFirstObjectByType<AudioListener>();
				clipPosition = _audioListener.transform;
			}
		}

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			switch (mode)
			{
			case AudioPlayOneShotInteractionMode.AudioSource:
				if (audioSource == null)
				{
					Debug.LogError("No AudioSource found, AudioPlayOneShotInteraction can not play, if you want an Audio Source to be created in runtime use one of the Transient modes instead");
				}
				else
				{
					audioSource.PlayOneShot(audioClip, volume);
				}
				break;
			case AudioPlayOneShotInteractionMode.Position2D:
				if (_audioListener == null)
				{
					_audioListener = Object.FindFirstObjectByType<AudioListener>();
					if (_audioListener == null)
					{
						Debug.LogError("No AudioListener found, AudioPlayOneShotInteraction can not play");
						break;
					}
					clipPosition = _audioListener.transform;
				}
				AudioSource.PlayClipAtPoint(audioClip, clipPosition.position, volume);
				break;
			case AudioPlayOneShotInteractionMode.Position3D:
				if (clipPosition == null)
				{
					Debug.LogError("No Clip Position found, AudioPlayOneShotInteraction can not play");
				}
				else
				{
					AudioSource.PlayClipAtPoint(audioClip, clipPosition.position, volume);
				}
				break;
			}
			OnEnd();
		}
	}
}
