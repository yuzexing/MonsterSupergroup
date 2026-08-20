using AstralShift.AstralShiftCreditsRenderer;
using AstralShift.Control.Controllers;
using AstralShift.Managers;
using AstralShift.UI;
using DG.Tweening;
using Rewired;
using UnityEngine;

namespace AstralShift.HellMaiden.Controllers
{
	public class CreditsMenuController : GameMenuController
	{
		[Header("Scroll Settings")]
		[SerializeField]
		private AutomaticScroll automaticScroll;

		[SerializeField]
		private AstralShift.AstralShiftCreditsRenderer.AstralShiftCreditsRenderer creditsRenderView;

		[SerializeField]
		private CanvasGroup canvasGroup;

		protected void Awake()
		{
			if (automaticScroll == null)
			{
				automaticScroll = GetComponent<AutomaticScroll>();
			}
			if (canvasGroup == null)
			{
				canvasGroup = GetComponent<CanvasGroup>();
			}
			canvasGroup.alpha = 0f;
			canvasGroup.blocksRaycasts = false;
			ControllerManager.Instance.Subscribe(this, init: true);
		}

		protected override void InitStateBehaviour()
		{
		}

		public override void Open()
		{
			canvasGroup.blocksRaycasts = true;
			creditsRenderView.InitializeCredits();
			base.Open();
			automaticScroll.ScrollTo(1f, instant: true);
		}

		protected override void OnClosingFinished()
		{
			base.OnClosingFinished();
			ControllerManager.Instance.YieldGameController();
		}

		public override void Close()
		{
			canvasGroup.blocksRaycasts = false;
			base.Close();
		}

		public override void UILeftStickVertical(InputActionEventData data)
		{
			float axis = data.GetAxis();
			if (Mathf.Approximately(axis, 0f))
			{
				automaticScroll.StopScrollbarAnimation();
			}
			else if (axis > 0f)
			{
				automaticScroll.AnimateScrollbarValueTop();
			}
			else if (axis < 0f)
			{
				automaticScroll.AnimateScrollbarValueBottom();
			}
		}

		public override void UICancelPressed(InputActionEventData data)
		{
			Close();
		}

		private void OnDestroy()
		{
			canvasGroup.DOKill();
			ControllerManager.Instance.UnSubscribe(this);
		}
	}
}
