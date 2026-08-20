using System;
using System.Collections.Generic;
using AstralShift.Control.Controllers;
using AstralShift.Helpers;
using AstralShift.Managers;
using AstralShift.Rendering;
using Cysharp.Threading.Tasks;
using FMODUnity;
// using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace AstralShift.HellMaiden.Controllers
{
	public class AnimatedSplashController : GameController
	{
		[Header("UI")]
		[SerializeField]
		private CanvasGroup canvasGroup;

		[SerializeField]
		private Animator animator;

		[SerializeField]
		private float showAnimDuration = 3f;

		[SerializeField]
		private float hideAnimDuration = 2f;

		[Header("Sound")]
		[SerializeField]
		private EventReference menuInSound;

		[SerializeField]
		private EventReference menuOutSound;

		private Animator _animator;

		private readonly int HideAnimHash = Animator.StringToHash("Hide");

		protected string eventName = "event:/sx/dlg/sx_dlg_vo";

		[SerializeField]
		private List<string> VALineId;

		public event Action OnAnyInputPressed;

		public event Action OnEnd;

		public override void Activate()
		{
			GameDirector.Instance.Player.StopMovement();
			PauseManager.Instance.PauseGame();
			ASRendererFeature.Instance?.EnableFullscreenBlurRenderPass(enable: true);
			Show();
		}

		public override void Deactivate()
		{
			ASRendererFeature.Instance?.EnableFullscreenBlurRenderPass(enable: false);
			PauseManager.Instance.ResumeGame();
		}

		private async void Show()
		{
			// DialogueManager.instance.gameObject.GetComponent<FmodProgramerEventPlayer>().PlayRandomDialogueFromList(eventName, VALineId, 1f);
			try
			{
				animator.enabled = false;
				canvasGroup.alpha = 0f;
				animator.enabled = true;
				RuntimeManager.PlayOneShot(menuInSound);
				await UniTask.Delay(TimeSpan.FromSeconds(showAnimDuration), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, base.destroyCancellationToken);
				OnAnyInputPressed += Hide;
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private async void Hide()
		{
			try
			{
				OnAnyInputPressed -= Hide;
				animator.Play(HideAnimHash);
				RuntimeManager.PlayOneShot(menuOutSound);
				await UniTask.Delay(TimeSpan.FromSeconds(hideAnimDuration), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, base.destroyCancellationToken);
				ControllerManager.Instance.YieldGameController();
				ControllerManager.Instance.UnSubscribe(this);
				this.OnEnd?.Invoke();
				UnityEngine.Object.Destroy(base.gameObject);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		public override void AnyInputDown()
		{
			this.OnAnyInputPressed?.Invoke();
		}

		public override void AnyMouseInputStateChanged(int button, bool pressed)
		{
			if (pressed)
			{
				this.OnAnyInputPressed?.Invoke();
			}
		}
	}
}
