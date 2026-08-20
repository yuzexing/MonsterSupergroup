using System;
using System.Collections;
using System.Collections.Generic;
using AstralShift.Helpers;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.UI
{
	public class AutomaticScroll : MonoBehaviour
	{
		private const int TOP_TEXTURE_INDEX = 0;

		private const int CENTER_TEXTURE_INDEX = 1;

		private const int BOTTOM_TEXTURE_INDEX = 2;

		private const float TOP_GRADIENT_THRESHOLD = 1f;

		private const float BOTTOM_GRADIENT_THRESHOLD = 0f;

		[Header("Core Components")]
		[SerializeField]
		protected ScrollRect scrollRect;

		[SerializeField]
		private RectTransform scrollContentRectTransform;

		[SerializeField]
		protected internal RectTransform viewportRectTransform;

		[Header("CONTROLS")]
		[Space(10f)]
		[Header("Scroll Buttons")]
		[SerializeField]
		protected CustomUIButton scrollUpButton;

		[SerializeField]
		protected CustomUIButton scrollDownButton;

		[Header("Scroll Slider")]
		[SerializeField]
		private Slider scrollSlider;

		[SerializeField]
		private Scrollbar scrollScrollbar;

		[SerializeField]
		protected bool instantScrollSlider;

		[Header("VISUAL EFFECTS")]
		[Space(10f)]
		[Header("Gradient Masks")]
		[SerializeField]
		private bool useGradientFlag;

		[Tooltip("Texture order: 0=Top, 1=Center, 2=Bottom")]
		[SerializeField]
		private List<Sprite> gradientRenderedTextureList;

		[SerializeField]
		private Image gradientTransparentMaskMenu;

		[Header("Gradient Fade Offsets")]
		[SerializeField]
		[Range(0f, 0.3f)]
		private float topGradientOffset = 0.08f;

		[SerializeField]
		[Range(0f, 0.3f)]
		private float bottomGradientOffset = 0.02f;

		[Header("SCROLL SETTINGS")]
		[Space(10f)]
		[SerializeField]
		private float scrollTime = 0.2f;

		[SerializeField]
		private float scrollMovementeAmount;

		[SerializeField]
		private float topPadding;

		[SerializeField]
		private float bottomPadding;

		[SerializeField]
		private float continuousScrollFactor;

		[SerializeField]
		private float continuousScrollButtonVelocity = 0.5f;

		[SerializeField]
		private CustomAnimationCurve scrollAnimationCurve;

		[Header("AUTOMATIC SCROLL SETTINGS")]
		[SerializeField]
		private float baseSpeed = 1f;

		[SerializeField]
		private float acceleration = 2f;

		[SerializeField]
		private float mouseWheelScrollSensitivity = 25f;

		private float sizeFactor;

		private Coroutine currentCoroutine;

		private Tween scrollTween;

		private bool onScrollEventLock;

		private float currentPos;

		private float targetPos;

		private float distance;

		private float duration;

		private VerticalLayoutGroup _verticalLayoutGroup;

		private GridLayoutGroup _gridLayoutGroup;

		private void Awake()
		{
			InitializeComponents();
			SetupButtonListeners();
			SetupSliderListener();
		}

		private void OnEnable()
		{
			scrollRect.scrollSensitivity = mouseWheelScrollSensitivity;
		}

		private void OnDisable()
		{
			CleanUpCoroutine();
			CleanUpTween();
		}

		private void OnDestroy()
		{
			CleanUpCoroutine();
			CleanUpTween();
			if (scrollUpButton != null)
			{
				scrollUpButton.onSubmit.RemoveAllListeners();
			}
			if (scrollDownButton != null)
			{
				scrollDownButton.onSubmit.RemoveAllListeners();
			}
			if (scrollSlider != null)
			{
				scrollSlider.onValueChanged.RemoveAllListeners();
			}
			if (scrollScrollbar != null)
			{
				scrollScrollbar.onValueChanged.RemoveAllListeners();
			}
		}

		private void InitializeComponents()
		{
			scrollContentRectTransform.TryGetComponent<VerticalLayoutGroup>(out _verticalLayoutGroup);
			scrollContentRectTransform.TryGetComponent<GridLayoutGroup>(out _gridLayoutGroup);
		}

		private void SetupButtonListeners()
		{
			scrollUpButton?.onSubmit.AddListener(delegate
			{
				ScrollUp(UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(scrollMovementeAmount));
			});
			scrollDownButton?.onSubmit.AddListener(delegate
			{
				ScrollDown(UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(scrollMovementeAmount));
			});
		}

		private void SetupSliderListener()
		{
			if (scrollSlider != null)
			{
				scrollSlider.onValueChanged.AddListener(delegate(float scrollValue)
				{
					ScrollTo(scrollValue, instantScrollSlider);
				});
			}
			if (scrollScrollbar != null)
			{
				scrollScrollbar.onValueChanged.AddListener(delegate(float scrollValue)
				{
					ScrollTo(scrollValue, instantScrollSlider);
				});
			}
			scrollRect.onValueChanged.AddListener(OnScroll);
			OnScroll(Vector2.one);
		}

		public void AnimateScrollbarValue(float targetValue, float speed, Action onComplete = null)
		{
			targetValue = Mathf.Clamp01(targetValue);
			currentPos = GetCurrentScrollPosition();
			distance = Mathf.Abs(targetValue - currentPos);
			duration = distance / speed;
			if (duration < 0.01f)
			{
				scrollRect.verticalNormalizedPosition = targetValue;
				onComplete?.Invoke();
				return;
			}
			scrollTween?.Kill();
			scrollTween = DOTween.To(() => scrollRect.verticalNormalizedPosition, delegate(float x)
			{
				scrollRect.verticalNormalizedPosition = x;
				if (scrollSlider != null)
				{
					scrollSlider.value = x;
				}
				if (scrollScrollbar != null)
				{
					scrollScrollbar.value = x;
				}
				OnScroll(new Vector2(0f, x));
			}, targetValue, duration).SetAutoKill(autoKillOnCompletion: true).SetUpdate(UpdateType.Normal, isIndependentUpdate: true)
				.OnStart(delegate
				{
					onScrollEventLock = true;
				})
				.OnComplete(delegate
				{
					onScrollEventLock = false;
					onComplete?.Invoke();
				})
				.OnKill(delegate
				{
					onScrollEventLock = false;
				});
		}

		public void AnimateScrollbarValueTop()
		{
			scrollTween?.Kill();
			currentPos = GetCurrentScrollPosition();
			targetPos = 1f;
			distance = Mathf.Abs(targetPos - currentPos);
			duration = distance / (baseSpeed + acceleration);
			scrollTween = DOTween.To(() => scrollRect.verticalNormalizedPosition, delegate(float x)
			{
				scrollRect.verticalNormalizedPosition = x;
				if (scrollSlider != null)
				{
					scrollSlider.value = x;
				}
				if (scrollScrollbar != null)
				{
					scrollScrollbar.value = x;
				}
				OnScroll(new Vector2(0f, x));
			}, targetPos, duration).SetEase(Ease.Linear).SetAutoKill(autoKillOnCompletion: true)
				.SetUpdate(UpdateType.Normal, isIndependentUpdate: true)
				.OnStart(delegate
				{
					onScrollEventLock = true;
				})
				.OnComplete(delegate
				{
					onScrollEventLock = false;
				})
				.OnKill(delegate
				{
					onScrollEventLock = false;
				});
		}

		public void AnimateScrollbarValueBottom()
		{
			scrollTween?.Kill();
			currentPos = GetCurrentScrollPosition();
			targetPos = 0f;
			distance = Mathf.Abs(targetPos - currentPos);
			duration = distance / (baseSpeed + acceleration);
			scrollTween = DOTween.To(() => scrollRect.verticalNormalizedPosition, delegate(float x)
			{
				scrollRect.verticalNormalizedPosition = x;
				if (scrollSlider != null)
				{
					scrollSlider.value = x;
				}
				if (scrollScrollbar != null)
				{
					scrollScrollbar.value = x;
				}
				OnScroll(new Vector2(0f, x));
			}, targetPos, duration).SetEase(Ease.Linear).SetAutoKill(autoKillOnCompletion: true)
				.SetUpdate(UpdateType.Normal, isIndependentUpdate: true)
				.OnStart(delegate
				{
					onScrollEventLock = true;
				})
				.OnComplete(delegate
				{
					onScrollEventLock = false;
				})
				.OnKill(delegate
				{
					onScrollEventLock = false;
				});
		}

		public void StopScrollbarAnimation()
		{
			scrollTween?.Kill();
		}

		public void RecalculateScrollContentSize()
		{
			CalculateScrollContentSize();
		}

		public void ScrollToSelectedObject(RectTransform objectRectTransform, float buff = 0f)
		{
			if (currentCoroutine != null)
			{
				StopCoroutine(currentCoroutine);
			}
			currentCoroutine = StartCoroutine(ScrollToSelectedObjectCoroutine(objectRectTransform, buff));
		}

		public void ScrollDown(float scrollHeight)
		{
			float num = ScrollCalc(scrollContentRectTransform.anchoredPosition.y + scrollHeight);
			if (scrollSlider != null)
			{
				scrollSlider?.SetValueWithoutNotify(num);
			}
			if (scrollScrollbar != null)
			{
				scrollScrollbar?.SetValueWithoutNotify(num);
			}
			PlayScrollTween(num);
		}

		public void ScrollUp(float scrollHeight)
		{
			float num = ScrollCalc(scrollContentRectTransform.anchoredPosition.y - scrollHeight);
			if (scrollSlider != null)
			{
				scrollSlider?.SetValueWithoutNotify(num);
			}
			if (scrollScrollbar != null)
			{
				scrollScrollbar?.SetValueWithoutNotify(num);
			}
			PlayScrollTween(num);
		}

		public void ScrollTo(float scrollValue, bool instant = false)
		{
			scrollValue = Math.Clamp(scrollValue, 0f, 1f);
			if (instant)
			{
				scrollRect.verticalNormalizedPosition = scrollValue;
			}
			PlayScrollTween(scrollValue);
		}

		public void ContinuousScroll(float scrollValue)
		{
			if (sizeFactor != 0f)
			{
				scrollRect.velocity = new Vector2(0f, (0f - scrollValue) * continuousScrollFactor);
			}
		}

		private float GetCurrentScrollPosition()
		{
			if (scrollRect != null)
			{
				return scrollRect.verticalNormalizedPosition;
			}
			return 0f;
		}

		private async void CalculateScrollContentSize()
		{
			await Awaitable.NextFrameAsync();
			await Awaitable.NextFrameAsync();
			float num = UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(viewportRectTransform.rect.height) + UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(bottomPadding) + UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(topPadding);
			float resAdjustedScreenSpaceOffset = UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(scrollContentRectTransform.rect.height);
			if (resAdjustedScreenSpaceOffset > num)
			{
				sizeFactor = resAdjustedScreenSpaceOffset / num;
				SetScrollControlsActive(active: true);
			}
			else
			{
				SetScrollControlsActive(active: false);
			}
		}

		private void SetScrollControlsActive(bool active)
		{
			if (scrollSlider != null)
			{
				scrollSlider?.gameObject.SetActive(active);
			}
			if (scrollScrollbar != null)
			{
				scrollScrollbar?.gameObject.SetActive(active);
			}
		}

		private IEnumerator ScrollToSelectedObjectCoroutine(RectTransform objectRectTransform, float buff = 0f)
		{
			yield return new WaitForEndOfFrame();
			yield return new WaitForEndOfFrame();
			Vector3[] array = new Vector3[4];
			objectRectTransform.GetWorldCorners(array);
			Vector3[] array2 = new Vector3[4];
			viewportRectTransform.GetWorldCorners(array2);
			float y = objectRectTransform.anchoredPosition.y;
			y = Mathf.Abs(y);
			if (array[1].y > array2[1].y)
			{
				float num = (objectRectTransform.pivot.y - 1f) * objectRectTransform.rect.size.y;
				y += num;
				if ((bool)_verticalLayoutGroup)
				{
					y -= (float)_verticalLayoutGroup.padding.bottom;
				}
				if ((bool)_gridLayoutGroup)
				{
					y -= (float)_gridLayoutGroup.padding.top;
				}
				y -= buff;
				PlayScrollTween(ScrollCalc(y));
			}
			else if (array[0].y < array2[0].y)
			{
				float num2 = objectRectTransform.pivot.y * objectRectTransform.rect.size.y;
				y += num2;
				if ((bool)_verticalLayoutGroup)
				{
					y += (float)_verticalLayoutGroup.padding.top;
				}
				if ((bool)_gridLayoutGroup)
				{
					y += (float)_gridLayoutGroup.padding.top;
				}
				y += buff;
				float scrollValue = ((!Mathf.Approximately(y, 0f) && !(y < viewportRectTransform.rect.height * 0.5f)) ? ScrollCalc(y - viewportRectTransform.rect.height) : 1f);
				PlayScrollTween(scrollValue);
			}
		}

		private float ScrollCalc(float height)
		{
			float num = Mathf.Clamp(height, 0f, scrollContentRectTransform.sizeDelta.y);
			return 1f - num / scrollContentRectTransform.sizeDelta.y;
		}

		private void PlayScrollTween(float scrollValue)
		{
			onScrollEventLock = true;
			scrollTween.Kill();
			scrollTween = DOTween.To(() => scrollRect.verticalNormalizedPosition, delegate(float x)
			{
				scrollRect.verticalNormalizedPosition = x;
			}, scrollValue, scrollTime);
			scrollTween.SetUpdate(UpdateType.Normal, isIndependentUpdate: true);
			scrollTween.SetEase(scrollAnimationCurve.GetEaseFunction());
			scrollTween.Play();
			scrollTween.onComplete = delegate
			{
				onScrollEventLock = false;
			};
		}

		private void OnScroll(Vector2 value)
		{
			float num = Math.Clamp(value.y, 0f, 1f);
			UpdateGradientMask(num);
			if (!onScrollEventLock)
			{
				UpdateScrollButtons(num);
				if (scrollSlider != null)
				{
					scrollSlider.SetValueWithoutNotify(num);
				}
				if (scrollScrollbar != null)
				{
					scrollScrollbar.SetValueWithoutNotify(num);
				}
			}
		}

		private void UpdateGradientMask(float scrollValue)
		{
			if (useGradientFlag)
			{
				bool flag = scrollValue >= 1f - topGradientOffset;
				bool flag2 = scrollValue <= 0f + bottomGradientOffset;
				if (flag && flag2)
				{
					gradientTransparentMaskMenu.sprite = gradientRenderedTextureList[1];
				}
				else if (flag)
				{
					gradientTransparentMaskMenu.sprite = gradientRenderedTextureList[2];
				}
				else if (flag2)
				{
					gradientTransparentMaskMenu.sprite = gradientRenderedTextureList[0];
				}
				else
				{
					gradientTransparentMaskMenu.sprite = gradientRenderedTextureList[1];
				}
			}
		}

		private void UpdateScrollButtons(float scrollValue)
		{
			if (scrollDownButton != null)
			{
				float targetAlpha = ((scrollValue <= 0f) ? 0f : 1f);
				CanvasGroup downCanvasGroup = scrollDownButton.CanvasGroup;
				if (downCanvasGroup == null)
				{
					downCanvasGroup = scrollDownButton.gameObject.AddComponent<CanvasGroup>();
				}
				if (!Mathf.Approximately(downCanvasGroup.alpha, targetAlpha))
				{
					DOTween.Kill(downCanvasGroup);
					downCanvasGroup.DOFade(targetAlpha, scrollTime * 0.5f).SetUpdate(UpdateType.Normal, isIndependentUpdate: true).OnComplete(delegate
					{
						downCanvasGroup.interactable = targetAlpha > 0.5f;
						downCanvasGroup.blocksRaycasts = targetAlpha > 0.5f;
					});
				}
			}
			if (!(scrollUpButton != null))
			{
				return;
			}
			float targetAlpha2 = ((scrollValue >= 1f) ? 0f : 1f);
			CanvasGroup upCanvasGroup = scrollUpButton.CanvasGroup;
			if (upCanvasGroup == null)
			{
				upCanvasGroup = scrollUpButton.gameObject.AddComponent<CanvasGroup>();
			}
			if (!Mathf.Approximately(upCanvasGroup.alpha, targetAlpha2))
			{
				DOTween.Kill(upCanvasGroup);
				upCanvasGroup.DOFade(targetAlpha2, scrollTime * 0.5f).SetUpdate(UpdateType.Normal, isIndependentUpdate: true).OnComplete(delegate
				{
					upCanvasGroup.interactable = targetAlpha2 > 0.5f;
					upCanvasGroup.blocksRaycasts = targetAlpha2 > 0.5f;
				});
			}
		}

		private void CleanUpCoroutine()
		{
			if (currentCoroutine != null)
			{
				StopCoroutine(currentCoroutine);
				currentCoroutine = null;
			}
		}

		private void CleanUpTween()
		{
			DOTween.Kill(this);
			scrollTween = null;
		}
	}
}
