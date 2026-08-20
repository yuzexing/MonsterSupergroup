using AstralShift.Cinematics;
using AstralShift.Control;
using AstralShift.Control.Controllers;
using AstralShift.HellMaiden.Scenes;
using AstralShift.HellMaiden.UI;
using AstralShift.Helpers;
using AstralShift.Managers;
using AstralShift.Rendering;
using FMODUnity;
using Rewired;
using UnityEngine;

namespace AstralShift.HellMaiden.Controllers
{
	public class VideoSceneController : UIController
	{
		[SerializeField]
		private CinematicPlayer videoPlayer;

		[SerializeField]
		private CanvasGroup videoCanvasGroup;

		[SerializeField]
		private CustomUnityUIPlayerControllerElementGlyph skipGlyph;

		[SerializeField]
		private float skipHoldTime = 1f;

		[SerializeField]
		private EventReference cutsceneSkip;

		[SerializeField]
		private SceneEnum nextScene;

		private bool _canSkip;

		private bool _requireFreshPress;

		public override void Activate()
		{
			InputHandler.EnableMenuInputs();
			ASRendererFeature.Instance.EnableFullscreenBlurRenderPass(enable: false);
			PointerManager.Instance.HideMouseCursor();
			videoPlayer.PreWarm();
			SceneMaster.Instance.OnSceneShowFinish += Play;
			_canSkip = false;
			videoCanvasGroup.alpha = 0f;
			videoCanvasGroup.interactable = false;
			TimerHoldInteractionTaskHelper.CancelAndDispose();
		}

		public override void Deactivate()
		{
			base.Deactivate();
			TimerHoldInteractionTaskHelper.CancelAndDispose();
		}

		private void Play()
		{
			skipGlyph.gameObject.SetActive(value: true);
			skipGlyph.SetHold(skipHoldTime);
			videoCanvasGroup.alpha = 1f;
			videoPlayer.SetOnVideoEndCallback(LoadNextScene);
			videoPlayer.StartVideo();
			_canSkip = true;
			_requireFreshPress = true;
		}

		private void Update()
		{
			if (_requireFreshPress)
			{
				Rewired.Player player = ReInput.players.GetPlayer(ControllerLifetime.playerId);
				if (player != null && !player.GetButton("UICancel"))
				{
					_requireFreshPress = false;
				}
			}
		}

		private void SkipVideo()
		{
			_canSkip = false;
			skipGlyph.gameObject.SetActive(value: false);
			videoPlayer.SkipVideo();
			RuntimeManager.PlayOneShot(cutsceneSkip);
		}

		private void LoadNextScene()
		{
			SceneMaster.Instance.LoadScene(nextScene);
		}

		public override void UICancelReleased(InputActionEventData data)
		{
			if (_canSkip)
			{
				_requireFreshPress = false;
				TimerHoldInteractionTaskHelper.CancelAndDispose();
			}
		}

		public override void UICancelHeld(InputActionEventData data)
		{
			if (_canSkip && !_requireFreshPress)
			{
				TimerHoldInteractionTaskHelper.ProcessHoldAsync(skipHoldTime, SkipVideo);
			}
		}

		private void OnDestroy()
		{
			ControllerManager.Instance.UnSubscribe(this);
		}
	}
}
