using Animancer;
using AstralShift.HellMaiden.Scenes;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

namespace AstralShift.HellMaiden.UI
{
	public class CircleTimeoutOverlay : MonoBehaviour
	{
		[SerializeField]
		private GameObject[] cracks;

		[SerializeField]
		private AnimancerComponent animancerComponent;

		[SerializeField]
		private ClipTransition defaultAnimation;

		[SerializeField]
		private ClipTransition startAnimation;

		[SerializeField]
		private Volume volume;

		[SerializeField]
		private CircleTimeoutOverlayVolumeProfiles volumeProfilesData;

		public void Initialize()
		{
			InitializeVolumeProfile();
			InitializeAnimation();
		}

		public void InitializeVolumeProfile()
		{
			VolumeProfile profile = volumeProfilesData.GetProfile(SceneMaster.Instance.CurrentSceneEnum);
			if (profile != null)
			{
				volume.sharedProfile = profile;
			}
		}

		public void InitializeAnimation()
		{
			animancerComponent.UpdateMode = AnimatorUpdateMode.UnscaledTime;
			animancerComponent.Play(defaultAnimation);
		}

		public void RunStartAnimation()
		{
			animancerComponent.Play(startAnimation);
		}
	}
}
