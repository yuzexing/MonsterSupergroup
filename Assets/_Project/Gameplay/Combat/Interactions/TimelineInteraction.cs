using AstralShift.Control.Controllers;
using AstralShift.FadeEffect;
using AstralShift.HellMaiden.Common;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Timeline;
using AstralShift.Helpers;
using AstralShift.Managers;
using AstralShift.QTI.Helpers.Attributes;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using Com.LuisPedroFonseca.ProCamera2D;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class TimelineInteraction : Interaction
	{
		public TimelineDirector timeline;

		private float fadeOutTime = 1f;

		private float fadeInTime = 1f;

		public bool inScene;

		[SerializeField]
		private bool OverridePosition;

		[ConditionalHide("OverridePosition", true)]
		public Transform position;

		public Direction directionToFace;

		public FadeEffectEnum entryFadeEffect;

		public FadeEffectEnum exitFadeEffect;

		private TimelineDirector timelineDirector;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			PauseManager.Instance.PausePausables();
			ControllerManager.Instance.OverrideGameController<NoInputGameController>();
			ScreenFader.Instance.FadeOut(onEnd: delegate
			{
				if (timeline == null)
				{
					Debug.LogError("Timeline is null");
				}
				else
				{
					PlayerMovement player = GameDirector.Instance.Player;
					if (player == null)
					{
						Debug.LogError("Player is null");
					}
					else if (ProCamera2D.Instance == null)
					{
						Debug.LogError("ProCamera2D is null");
					}
					else if (ScreenFader.Instance == null)
					{
						Debug.LogError("ScreenFader is null");
					}
					else if (ControllerManager.Instance == null)
					{
						Debug.LogError("ControllerManager.Instance is null");
					}
					else
					{
						ActivateCutscene();
						if (timelineDirector == null)
						{
							Debug.LogError("TimelineDirector is null");
						}
						else if (timelineDirector.StartTransformPlayer == null)
						{
							Debug.LogError("TimelineDirector.StartTransformPlayer is null");
						}
						else
						{
							player.transform.position = timelineDirector.StartTransformPlayer.position;
							player.SetDirectionImmediate(directionToFace.ToVector2());
							ProCamera2D.Instance.CenterOnTargets();
							if (timelineDirector.overwriteFadeIn)
							{
								ControllerManager.Instance.YieldGameController();
								PauseManager.Instance.ResumePausables();
								StartCoroutine(Wait.SetFrameTimeout(2, delegate
								{
									timelineDirector.Play();
								}));
							}
							else
							{
								ScreenFader.Instance.SetFadeIn(timelineDirector.entryFade);
								ScreenFader.Instance.FadeIn(exitFadeEffect, fadeInTime, delegate
								{
									ControllerManager.Instance.YieldGameController();
									PauseManager.Instance.ResumePausables();
									timelineDirector.Play();
								});
							}
						}
					}
				}
			}, effect: entryFadeEffect, duration: fadeOutTime);
			OnEnd();
		}

		private void ActivateCutscene()
		{
			if (!inScene)
			{
				timelineDirector = Object.Instantiate(timeline, OverridePosition ? position.position : timeline.transform.position, Quaternion.identity, base.transform.parent);
				timelineDirector.name = timeline.name;
				timelineDirector.gameObject.SetActive(value: true);
				return;
			}
			timeline.gameObject.SetActive(value: true);
			timelineDirector = timeline;
			if (OverridePosition)
			{
				timelineDirector.transform.position = position.position;
			}
		}
	}
}
