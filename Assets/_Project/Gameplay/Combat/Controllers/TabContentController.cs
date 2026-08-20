using System;
using Animancer;
using AstralShift.UI;
using UnityEngine;

public abstract class TabContentController : MonoBehaviour
{
	public AnimancerComponent menuAnimator;

	[SerializeField]
	protected ClipTransition openAnimation;

	[SerializeField]
	protected ClipTransition closeAnimation;

	[SerializeField]
	private new string name;

	[SerializeField]
	private string description;

	public UISelectable firstSelected;

	[HideInInspector]
	public UISelectable previousSelected;

	[HideInInspector]
	public UISelectable currentSelected;

	public Action onLeft;

	public Action onRight;

	public string Name => name;

	public string Description => description;

	public virtual void Init()
	{
		ref Action onEnd = ref closeAnimation.Events.OnEnd;
		onEnd = (Action)Delegate.Combine(onEnd, (Action)delegate
		{
			base.gameObject.SetActive(value: false);
		});
		menuAnimator.UpdateMode = AnimatorUpdateMode.UnscaledTime;
		base.gameObject.SetActive(value: false);
	}

	public virtual void Open(bool instant = false)
	{
		base.gameObject.SetActive(value: true);
		ref Action onEnd = ref openAnimation.Events.OnEnd;
		onEnd = (Action)Delegate.Combine(onEnd, new Action(OnOpeningFinished));
		AnimancerState animancerState = menuAnimator.Play(openAnimation, openAnimation.FadeDuration);
		if (instant)
		{
			animancerState.MoveTime(1f, normalized: true);
		}
	}

	protected virtual void OnOpeningFinished()
	{
		ref Action onEnd = ref openAnimation.Events.OnEnd;
		onEnd = (Action)Delegate.Remove(onEnd, new Action(OnOpeningFinished));
	}

	public virtual void Close(bool instant = false)
	{
		openAnimation.Events.OnEnd = null;
		ref Action onEnd = ref closeAnimation.Events.OnEnd;
		onEnd = (Action)Delegate.Combine(onEnd, new Action(OnClosingFinished));
		AnimancerState animancerState = menuAnimator.Play(closeAnimation, closeAnimation.FadeDuration);
		if (instant)
		{
			animancerState.MoveTime(1f, normalized: true);
		}
	}

	protected virtual void OnClosingFinished()
	{
		ref Action onEnd = ref closeAnimation.Events.OnEnd;
		onEnd = (Action)Delegate.Remove(onEnd, new Action(OnClosingFinished));
	}
}
