using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Animancer;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class OverflowBar : StatusBar
{
	[Serializable]
	public struct OverflowTresholds
	{
		public float overflowTreshold;

		public Sprite bar;

		public ClipTransition fireAnimationClip;
	}

	private int currentOverflowIndex;

	public Image overflowImage;

	public List<OverflowTresholds> overflowBars;

	public float overflowTime = 1f;

	private Color alpha1 = new Color(1f, 1f, 1f, 1f);

	private Color alpha0 = new Color(1f, 1f, 1f, 0f);

	[Header("Fire Animancer")]
	[SerializeField]
	private bool fireAnimation;

	[SerializeField]
	private AnimancerComponent fireAnimancerComponent;

	[SerializeField]
	private Animator fireAnimatorComponent;

	private Tween overflowTween;

	private Tween fireTween;

	private Coroutine fireCoroutine;

	public override void InitializeBar(float maxValue)
	{
		base.InitializeBar(maxValue);
		OrderTresholds();
		UpdateOverflow();
		overflowImage.color = alpha0;
	}

	public void OrderTresholds()
	{
		overflowBars = overflowBars.OrderBy((OverflowTresholds c) => c.overflowTreshold).ToList();
	}

	public override void StatusChange(float newValue)
	{
		base.StatusChange(newValue);
		UpdateOverflow();
	}

	public override void StatusChangePercentage(float targetFill)
	{
		base.StatusChangePercentage(targetFill);
		UpdateOverflow();
	}

	private void UpdateOverflow()
	{
		int num = 0;
		for (int i = 0; i < overflowBars.Count; i++)
		{
			if (currentValue >= overflowBars[i].overflowTreshold)
			{
				num = i;
			}
		}
		if (num == currentOverflowIndex)
		{
			return;
		}
		if (num > currentOverflowIndex)
		{
			overflowImage.sprite = overflowBars[num].bar;
			overflowImage.fillAmount = topBar.fillAmount;
			overflowImage.color = alpha0;
			overflowImage.gameObject.SetActive(value: true);
			overflowTween = overflowImage.DOFade(1f, overflowTime);
			overflowTween.SetUpdate(UpdateType.Late, unscaledTime);
			overflowTween.OnComplete(DisableOverflowImage);
			overflowTween.Restart();
			if (fireAnimation)
			{
				if (fireCoroutine != null)
				{
					StopCoroutine(fireCoroutine);
				}
				fireCoroutine = StartCoroutine(FireChangeAnimation(num, currentOverflowIndex));
			}
			currentOverflowIndex = num;
			return;
		}
		topBar.sprite = overflowBars[num].bar;
		overflowImage.sprite = overflowBars[currentOverflowIndex].bar;
		overflowImage.fillAmount = topBar.fillAmount;
		overflowImage.color = alpha1;
		overflowImage.gameObject.SetActive(value: true);
		overflowTween = overflowImage.DOFade(0f, overflowTime);
		overflowTween.SetUpdate(UpdateType.Late, unscaledTime);
		overflowTween.OnComplete(DisableOverflowImage);
		overflowTween.Restart();
		if (fireAnimation)
		{
			if (fireCoroutine != null)
			{
				StopCoroutine(fireCoroutine);
			}
			fireCoroutine = StartCoroutine(FireChangeAnimation(num, currentOverflowIndex));
		}
		currentOverflowIndex = num;
	}

	private IEnumerator FireChangeAnimation(int indexToGoTo, int currentIndex)
	{
		while (indexToGoTo != currentIndex)
		{
			fireAnimancerComponent.Layers[0].Stop();
			_ = overflowBars[currentIndex].fireAnimationClip;
			ClipTransition fireAnimationClip;
			if (indexToGoTo < currentIndex)
			{
				fireAnimationClip = overflowBars[currentIndex].fireAnimationClip;
				currentIndex--;
				fireAnimationClip.NormalizedStartTime = 1f;
				fireAnimationClip.Speed = -1f;
			}
			else
			{
				currentIndex++;
				fireAnimationClip = overflowBars[currentIndex].fireAnimationClip;
				fireAnimationClip.NormalizedStartTime = 0f;
				fireAnimationClip.Speed = 1f;
			}
			fireAnimancerComponent.Layers[0].Play(fireAnimationClip);
			yield return new WaitForSeconds(fireAnimationClip.Length);
		}
	}

	private void DisableOverflowImage()
	{
		topBar.sprite = overflowBars[currentOverflowIndex].bar;
		overflowImage.gameObject.SetActive(value: false);
	}
}
