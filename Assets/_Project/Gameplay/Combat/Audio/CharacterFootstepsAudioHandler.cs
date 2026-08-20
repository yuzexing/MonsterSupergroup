using AstralShift.Rendering;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Audio
{
	public class CharacterFootstepsAudioHandler : MonoBehaviour
	{
		[SerializeField]
		private EventReference footstepsEvent;

		private EventInstance _footstepsFirstInstance;

		private EventInstance _footstepsSecondInstance;

		private bool _isPlayingFirstInstance;

		private const string FootstepModeParameterName = "Movement";

		private const string FootstepSurfaceParameterName = "Surface";

		private void Start()
		{
			try
			{
				if (!footstepsEvent.IsNull)
				{
					_footstepsFirstInstance = RuntimeManager.CreateInstance(footstepsEvent);
					_footstepsSecondInstance = RuntimeManager.CreateInstance(footstepsEvent);
				}
			}
			catch (EventNotFoundException ex)
			{
				Debug.LogWarning(ex.Message, this);
			}
		}

		public void PlayFootstep(Transform target)
		{
			if (!_footstepsFirstInstance.isValid() || !_footstepsSecondInstance.isValid())
			{
				return;
			}
			int greyScaleValueFromPosition = MaterialGreyScaleManager.Instance.GetGreyScaleValueFromPosition(target.position);
			if (greyScaleValueFromPosition != -1)
			{
				if (_isPlayingFirstInstance)
				{
					_footstepsSecondInstance.setParameterByName("Movement", 1f);
					_footstepsSecondInstance.setParameterByName("Surface", greyScaleValueFromPosition);
					_footstepsSecondInstance.set3DAttributes(target.To3DAttributes());
					_footstepsSecondInstance.start();
					_isPlayingFirstInstance = false;
				}
				else
				{
					_footstepsFirstInstance.setParameterByName("Movement", 1f);
					_footstepsFirstInstance.setParameterByName("Surface", greyScaleValueFromPosition);
					_footstepsFirstInstance.set3DAttributes(target.To3DAttributes());
					_footstepsFirstInstance.start();
					_isPlayingFirstInstance = true;
				}
			}
		}

		private void OnDestroy()
		{
			if (_footstepsFirstInstance.isValid())
			{
				_footstepsFirstInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
				_footstepsFirstInstance.release();
			}
			if (_footstepsSecondInstance.isValid())
			{
				_footstepsSecondInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
				_footstepsSecondInstance.release();
			}
		}
	}
}
