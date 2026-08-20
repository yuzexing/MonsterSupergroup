using System.Collections;
using AstralShift.QTI.Interactors;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace AstralShift.QTI.Interactions.FMODIntegration
{
	public class FMODPlayEventInteraction : Interaction
	{
		public enum FMODPlayEventInteractionAction
		{
			Play = 0,
			Stop = 1
		}

		public FMODPlayEventInteractionAction action;

		public StudioEventEmitter studioEventEmitter;

		[Tooltip("Interaction will only call End Interactions when event has finished playing, does not work with looped sounds")]
		public bool waitForEventFinish;

		private EventInstance _eventInstance;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			switch (action)
			{
			case FMODPlayEventInteractionAction.Play:
				studioEventEmitter.Play();
				if (waitForEventFinish)
				{
					_eventInstance = studioEventEmitter.EventInstance;
					StartCoroutine(WaitForEventFinish());
				}
				break;
			case FMODPlayEventInteractionAction.Stop:
				studioEventEmitter.Stop();
				break;
			}
			if (!waitForEventFinish)
			{
				OnEnd();
			}
		}

		private IEnumerator WaitForEventFinish()
		{
			PLAYBACK_STATE state;
			do
			{
				yield return new WaitForEndOfFrame();
				if (!_eventInstance.isValid())
				{
					break;
				}
				_eventInstance.getPlaybackState(out state);
			}
			while (state != PLAYBACK_STATE.STOPPED);
			OnEnd();
		}
	}
}
