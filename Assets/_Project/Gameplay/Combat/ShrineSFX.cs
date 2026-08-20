using FMOD.Studio;
using FMODUnity;
using UnityEngine;

public class ShrineSFX : MonoBehaviour
{
	[SerializeField]
	private EventReference chargingSoundReference;

	[SerializeField]
	private EventReference dechargingSoundReference;

	[SerializeField]
	private EventReference poweredSoundReference;

	[SerializeField]
	private EventReference slamSoundReference;

	private EventInstance chargingSoundInstance;

	private EventDescription chargingSoundDescription;

	private EventInstance dechargingSoundInstance;

	private EventDescription dechargingSoundDescription;

	[SerializeField]
	private GameObject shrineSoundPosition;

	private float lengthFactor = 1f;

	private int chargingSoundPosition;

	private int dechargingSoundPosition;

	private int dechargingSoundLength;

	private int chargingSoundLength;

	private void Start()
	{
		chargingSoundInstance = RuntimeManager.CreateInstance(chargingSoundReference);
		chargingSoundInstance.getDescription(out chargingSoundDescription);
		chargingSoundInstance.set3DAttributes(shrineSoundPosition.To3DAttributes());
		chargingSoundDescription.getLength(out chargingSoundLength);
		dechargingSoundInstance = RuntimeManager.CreateInstance(dechargingSoundReference);
		dechargingSoundInstance.getDescription(out dechargingSoundDescription);
		dechargingSoundInstance.set3DAttributes(shrineSoundPosition.To3DAttributes());
		dechargingSoundDescription.getLength(out dechargingSoundLength);
		lengthFactor = chargingSoundLength / dechargingSoundLength;
	}

	public void ChargingShrineSound()
	{
		dechargingSoundInstance.getTimelinePosition(out dechargingSoundPosition);
		dechargingSoundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
		chargingSoundInstance.start();
		chargingSoundInstance.setTimelinePosition(Mathf.Clamp(chargingSoundPosition - dechargingSoundLength * dechargingSoundPosition / chargingSoundLength, 0, chargingSoundLength));
	}

	public void DechargingShrineSound()
	{
		chargingSoundInstance.getTimelinePosition(out chargingSoundPosition);
		chargingSoundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
		dechargingSoundInstance.start();
	}

	public void PoweredShrineSound()
	{
		StopSoundInstance();
		RuntimeManager.PlayOneShotAttached(poweredSoundReference, shrineSoundPosition);
	}

	public void StopSoundInstance()
	{
		chargingSoundPosition = 0;
		dechargingSoundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
		chargingSoundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
	}

	public void SlamShrineSound()
	{
		RuntimeManager.PlayOneShotAttached(slamSoundReference, shrineSoundPosition);
	}

	private void OnDestroy()
	{
		chargingSoundInstance.release();
		dechargingSoundInstance.release();
	}
}
