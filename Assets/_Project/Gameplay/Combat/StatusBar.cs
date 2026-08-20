using System.Collections;
using Animancer;
using AstralShift.Helpers.Attributes;
using AstralShift.QTI.Helpers.Attributes;
using Coffee.UISoftMask;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class StatusBar : MonoBehaviour
{
	[ReadOnly]
	[SerializeField]
	protected float maxValue;

	[ReadOnly]
	[SerializeField]
	protected float currentValue;

	[ReadOnly]
	[SerializeField]
	protected float currentPercentage;

	[SerializeField]
	protected Image topBar;

	[SerializeField]
	protected Image bottomBar;

	[SerializeField]
	private SoftMask topBarMask;

	[Header("Animation Options")]
	[SerializeField]
	private bool startFromZero;

	[Tooltip("Select if you want the bar to self initialize with a max value of 100")]
	[SerializeField]
	private bool selfStartBar;

	[SerializeField]
	private bool animateRise;

	[SerializeField]
	private bool animateFall;

	[SerializeField]
	private bool mantainPercentage;

	[SerializeField]
	private AnimationCurve animationCurve;

	[SerializeField]
	private bool haveMinimum;

	[SerializeField]
	protected bool unscaledTime;

	[Header("Blinking")]
	[SerializeField]
	private bool onValueDeacreaseBlink;

	[SerializeField]
	private bool onValueRiseBlink;

	[SerializeField]
	private bool useBlinkAnimation;

	[SerializeField]
	private AnimancerComponent animancerComponent;

	[FormerlySerializedAs("blinkAnimation")]
	[SerializeField]
	private ClipTransition blinkDeacreaseAnimation;

	[SerializeField]
	private ClipTransition blinkRiseAnimation;

	[SerializeField]
	private ClipTransition idleAnimation;

	[SerializeField]
	private ClipTransition lowPercentAnimation;

	[SerializeField]
	private Image blinkBar;

	[SerializeField]
	private Color blinkColor = Color.red;

	[SerializeField]
	private float blinkDuration = 0.5f;

	[SerializeField]
	private bool animateLowPercentageBlinking;

	[SerializeField]
	private float timeBetweenBlinks;

	[SerializeField]
	private Color lowBlinkColor = Color.white;

	[FormerlySerializedAs("minPercentage")]
	[ConditionalHide("haveMinimum", true)]
	[SerializeField]
	[Min(0f)]
	private float minPercentageForBlinking;

	[ConditionalHide("haveMinimum", true)]
	[SerializeField]
	[Min(0f)]
	private float minPercentageThatsShown;

	[ConditionalHide("haveMinimum", true)]
	[SerializeField]
	private bool blinkWhenAtMin;

	private Tween BarTween;

	private Color originalColor;

	private WaitForSeconds blinkWait;

	protected readonly int HitEffectColorSID = Shader.PropertyToID("_HitEffectColor");

	protected readonly int HitEffectBlendSID = Shader.PropertyToID("_HitEffectBlend");

	[Header("Fill Remap")]
	[Tooltip("Maps the 0..1 data ratio onto the visible fill range, when the fill sprite/rect doesn't reach the frame edges.")]
	[SerializeField]
	private bool remapFill;

	[SerializeField]
	[Range(0f, 1f)]
	private float emptyFill;

	[SerializeField]
	[Range(0f, 1f)]
	private float fullFill = 1f;

	private Coroutine blinkingCoroutine;

	private Coroutine blinkDeacreaseCoroutine;

	private Coroutine blinkRiseCoroutine;

	private bool isLowPercentBlinking;

	private float ToVisualFill(float dataRatio)
	{
		if (!remapFill)
		{
			return dataRatio;
		}
		return Mathf.Lerp(emptyFill, fullFill, dataRatio);
	}

	protected virtual void Awake()
	{
		originalColor = topBar.color;
		topBar.fillAmount = 1f;
		blinkBar?.gameObject.SetActive(value: false);
		currentValue = 0f;
		currentPercentage = 0f;
		blinkWait = new WaitForSeconds(timeBetweenBlinks);
		if (bottomBar != null && !animateFall && !animateRise)
		{
			bottomBar.gameObject.SetActive(value: false);
		}
		else
		{
			bottomBar.fillAmount = 1f;
		}
		if (selfStartBar)
		{
			InitializeBar(100f);
		}
	}

	private void OnDestroy()
	{
		BarTween?.Kill();
	}

	public virtual void InitializeBar(float maxValue)
	{
		if (!startFromZero)
		{
			currentValue = maxValue;
			currentPercentage = 1f;
		}
		else
		{
			currentValue = 0f;
			currentPercentage = 0f;
		}
		this.maxValue = maxValue;
		blinkBar?.gameObject.SetActive(value: false);
		float fillAmount = currentValue / maxValue;
		topBar.fillAmount = fillAmount;
		bottomBar.fillAmount = fillAmount;
	}

	public virtual void SetMaxValue(float maxValue)
	{
		this.maxValue = maxValue;
		if (mantainPercentage)
		{
			currentValue = maxValue * currentPercentage;
			StatusChange(currentValue);
		}
		else
		{
			currentPercentage = currentValue / maxValue;
		}
	}

	public virtual void Hide()
	{
		base.gameObject.SetActive(value: false);
	}

	public virtual void Show()
	{
		base.gameObject.SetActive(value: true);
	}

	public float AnimateLowPercentageBlink(float targetFill)
	{
		if (targetFill <= minPercentageForBlinking)
		{
			if (animateLowPercentageBlinking && !isLowPercentBlinking)
			{
				animancerComponent?.Layers[0].Play(lowPercentAnimation, 0f);
				isLowPercentBlinking = true;
			}
			if (targetFill <= minPercentageThatsShown)
			{
				targetFill = minPercentageThatsShown;
			}
		}
		if (isLowPercentBlinking && targetFill > minPercentageForBlinking)
		{
			topBar.color = originalColor;
			animancerComponent?.Layers[0].Play(idleAnimation, 0f);
			isLowPercentBlinking = false;
		}
		return targetFill;
	}

	public virtual void StatusChange(float newValue)
	{
		if (base.gameObject.activeInHierarchy)
		{
			float targetFill = newValue / maxValue;
			targetFill = AnimateLowPercentageBlink(targetFill);
			float num = ToVisualFill(targetFill);
			Image image = ((currentValue >= newValue) ? topBar : bottomBar);
			Image image2 = ((currentValue >= newValue) ? bottomBar : topBar);
			image.fillAmount = num;
			if (CheckIfAnimate(image2, num))
			{
				AnimateBar(num, image2);
			}
			else
			{
				image.fillAmount = num;
				image2.fillAmount = num;
			}
			currentValue = newValue;
			currentPercentage = targetFill;
		}
	}

	public virtual void StatusChangePercentage(float targetFill)
	{
		if (base.gameObject.activeInHierarchy)
		{
			targetFill = AnimateLowPercentageBlink(targetFill);
			Image obj = ((currentPercentage >= targetFill) ? topBar : bottomBar);
			Image bar = ((currentPercentage >= targetFill) ? bottomBar : topBar);
			obj.fillAmount = targetFill;
			if (CheckIfAnimate(bar, targetFill))
			{
				AnimateBar(targetFill, bar);
			}
			else
			{
				topBar.fillAmount = targetFill;
				bottomBar.fillAmount = targetFill;
			}
			currentPercentage = targetFill;
			currentValue = targetFill * maxValue;
		}
	}

	private void AnimateBar(float percentage, Image bar)
	{
		BarTween?.Kill();
		BarTween = DOTween.To(() => bar.fillAmount, delegate(float x)
		{
			bar.fillAmount = x;
		}, percentage, 0.5f);
		BarTween.SetUpdate(UpdateType.Normal, unscaledTime);
		if (animationCurve.keys.Length != 0)
		{
			BarTween.SetEase(animationCurve);
		}
		BarTween.Restart();
	}

	private bool CheckIfAnimate(Image bar, float targetFill)
	{
		if (bar.fillAmount < targetFill && animateRise)
		{
			if (onValueRiseBlink)
			{
				if (blinkRiseCoroutine != null)
				{
					StopCoroutine(blinkRiseCoroutine);
				}
				animancerComponent?.Layers[0].Play(idleAnimation, 0f);
				blinkRiseCoroutine = StartCoroutine(OnValueRiseBlinkingCoroutine());
			}
			AnimateLowPercentageBlink(targetFill);
			return true;
		}
		if (bar.fillAmount > targetFill && animateFall)
		{
			if (onValueDeacreaseBlink)
			{
				if (blinkDeacreaseCoroutine != null)
				{
					StopCoroutine(blinkDeacreaseCoroutine);
				}
				blinkDeacreaseCoroutine = StartCoroutine(OnValueDeacreseBlinkingCoroutine(targetFill));
			}
			return true;
		}
		return false;
	}

	private IEnumerator OnValueDeacreseBlinkingCoroutine(float targetFill)
	{
		WaitForSeconds waitForSeconds;
		if (useBlinkAnimation)
		{
			animancerComponent.Stop();
			animancerComponent.Layers[0].Play(blinkDeacreaseAnimation, 0f);
			waitForSeconds = new WaitForSeconds(blinkDeacreaseAnimation.Length);
			Debug.Log("STATUS_BAR blink");
		}
		else
		{
			blinkBar?.gameObject.SetActive(value: true);
			SetBlinkShaderValue(HitEffectBlendSID, 1f);
			SetBlinkShaderValue(HitEffectColorSID, blinkColor);
			waitForSeconds = new WaitForSeconds(blinkDuration);
		}
		yield return waitForSeconds;
		if (useBlinkAnimation)
		{
			isLowPercentBlinking = false;
		}
		AnimateLowPercentageBlink(targetFill);
		Debug.Log("STATUS_BAR targetFill = " + targetFill);
		blinkBar?.gameObject.SetActive(value: false);
		ResetHurtBlinkColor();
	}

	private IEnumerator OnValueRiseBlinkingCoroutine()
	{
		WaitForSeconds waitForSeconds;
		if (useBlinkAnimation)
		{
			animancerComponent.Stop();
			animancerComponent.Layers[0].Play(blinkRiseAnimation, 0f);
			waitForSeconds = new WaitForSeconds(blinkRiseAnimation.Length);
		}
		else
		{
			blinkBar?.gameObject.SetActive(value: true);
			SetBlinkShaderValue(HitEffectBlendSID, 1f);
			SetBlinkShaderValue(HitEffectColorSID, blinkColor);
			waitForSeconds = new WaitForSeconds(blinkDuration);
		}
		yield return waitForSeconds;
		blinkBar?.gameObject.SetActive(value: false);
		ResetHurtBlinkColor();
	}

	public virtual void ResetHurtBlinkColor()
	{
		SetBlinkShaderValue(HitEffectBlendSID, 0f);
		SetBlinkShaderValue(HitEffectColorSID, Color.white);
	}

	private void SetBlinkShaderValue(int id, float value)
	{
		blinkBar.material.SetFloat(id, value);
	}

	private void SetBlinkShaderValue(int id, Color value)
	{
		blinkBar.material.SetColor(id, value);
	}
}
