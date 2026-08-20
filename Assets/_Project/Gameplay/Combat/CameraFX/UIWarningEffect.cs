using AstralShift.HellMaiden.Scenes;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.CameraFX
{
	public class UIWarningEffect : FullscreenEffect
	{
		[SerializeField]
		private Animator animator;

		private int warningAnimHash = Animator.StringToHash("Warning");

		[SerializeField]
		private EventReference warningEvent;

		private EventInstance warningInstance;

		private void Start()
		{
			SceneMaster.Instance.OnSceneHideFinishPersist += Disable;
		}

		public override void Trigger()
		{
		}

		public override void Enable()
		{
			animator.SetBool(warningAnimHash, value: true);
			warningInstance = RuntimeManager.CreateInstance(warningEvent);
			warningInstance.start();
		}

		public override void Disable()
		{
			animator.SetBool(warningAnimHash, value: false);
			warningInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
			warningInstance.release();
		}
	}
}
