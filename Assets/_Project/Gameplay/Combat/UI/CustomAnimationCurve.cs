using System;
using DG.Tweening;
using DG.Tweening.Core.Easing;
using UnityEngine;

[Serializable]
public class CustomAnimationCurve
{
	public bool useOwnAnimationCurve;

	public Ease ease = Ease.Linear;

	public AnimationCurve animationCurve;

	private EaseCurve _animationCurveEase;

	public Tween AddEase(Tween tween)
	{
		if (useOwnAnimationCurve && animationCurve.keys.Length != 0)
		{
			return tween.SetEase(animationCurve);
		}
		return tween.SetEase(ease);
	}

	public float EasePercentage(float t)
	{
		if (useOwnAnimationCurve && animationCurve.keys.Length != 0)
		{
			return EaseManager.Evaluate(Ease.INTERNAL_Custom, new EaseCurve(animationCurve).Evaluate, t, 1f, DOTween.defaultEaseOvershootOrAmplitude, DOTween.defaultEasePeriod);
		}
		return EaseManager.Evaluate(ease, null, t, 1f, DOTween.defaultEaseOvershootOrAmplitude, DOTween.defaultEasePeriod);
	}

	public EaseFunction GetEaseFunction()
	{
		if (useOwnAnimationCurve && animationCurve.keys.Length != 0)
		{
			if (_animationCurveEase == null)
			{
				_animationCurveEase = new EaseCurve(animationCurve);
			}
			return _animationCurveEase.Evaluate;
		}
		return EaseManager.ToEaseFunction(ease);
	}

	public float InvertEase(float value, int iterations = 20)
	{
		float num = 0.5f;
		float num2 = 0.25f;
		for (int i = 0; i < iterations; i++)
		{
			num = ((!(EasePercentage(num) < value)) ? (num - num2) : (num + num2));
			num2 *= 0.5f;
		}
		return Mathf.Clamp01(num);
	}
}
