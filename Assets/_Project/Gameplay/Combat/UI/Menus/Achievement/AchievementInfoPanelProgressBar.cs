using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Menus.Achievement
{
	public class AchievementInfoPanelProgressBar : MonoBehaviour
	{
		[Header("References")]
		[SerializeField]
		private Slider sliderProgress;

		[SerializeField]
		private Image fillImage;

		[SerializeField]
		private TMP_Text progressText;

		[Header("Settings")]
		[SerializeField]
		private float currentProgress;

		[SerializeField]
		private float maxProgress = 100f;

		[SerializeField]
		private bool smoothTransition = true;

		[SerializeField]
		private float smoothSpeed = 5f;

		private float targetProgress;

		private Coroutine smoothRoutine;

		private bool hasInitialized;

		public event Action<float> OnProgressChanged;

		public event Action OnProgressCompleted;

		private void Start()
		{
			if (!hasInitialized)
			{
				targetProgress = currentProgress;
				if (sliderProgress != null)
				{
					sliderProgress.minValue = 0f;
					sliderProgress.maxValue = maxProgress;
					sliderProgress.value = currentProgress;
				}
				if (fillImage != null)
				{
					fillImage.fillAmount = currentProgress / maxProgress;
				}
				UpdateProgressText();
				CheckZeroAndUpdateFill();
				hasInitialized = true;
			}
		}

		public void Initialize(float current, float total)
		{
			if (total <= 0f)
			{
				total = 100f;
			}
			if (smoothRoutine != null)
			{
				StopCoroutine(smoothRoutine);
				smoothRoutine = null;
			}
			maxProgress = total;
			currentProgress = Mathf.Clamp(current, 0f, maxProgress);
			targetProgress = currentProgress;
			if (sliderProgress != null)
			{
				sliderProgress.minValue = 0f;
				sliderProgress.maxValue = maxProgress;
				sliderProgress.value = currentProgress;
			}
			if (fillImage != null)
			{
				fillImage.fillAmount = currentProgress / maxProgress;
			}
			UpdateProgressText();
			CheckZeroAndUpdateFill();
			hasInitialized = true;
			if (IsComplete())
			{
				this.OnProgressCompleted?.Invoke();
			}
		}

		public void SetProgress(float value)
		{
			targetProgress = Mathf.Clamp(value, 0f, maxProgress);
			currentProgress = targetProgress;
			if (smoothTransition)
			{
				StartSmoothTransition();
			}
			else
			{
				ApplyProgressImmediate();
			}
		}

		public void SetTotal(float total)
		{
			if (total <= 0f)
			{
				total = 100f;
			}
			maxProgress = total;
			if (sliderProgress != null)
			{
				sliderProgress.maxValue = maxProgress;
			}
			SetProgress(currentProgress);
		}

		public void AddProgress(float amount)
		{
			SetProgress(currentProgress + amount);
		}

		private void CheckZeroAndUpdateFill()
		{
			if (fillImage != null && sliderProgress != null)
			{
				fillImage.enabled = !Mathf.Approximately(sliderProgress.value, 0f);
			}
		}

		private void StartSmoothTransition()
		{
			if (smoothRoutine != null)
			{
				StopCoroutine(smoothRoutine);
			}
			smoothRoutine = StartCoroutine(SmoothTransitionRoutine());
		}

		private IEnumerator SmoothTransitionRoutine()
		{
			while (true)
			{
				bool flag = true;
				bool flag2 = true;
				if (sliderProgress != null)
				{
					sliderProgress.value = Mathf.Lerp(sliderProgress.value, targetProgress, Time.deltaTime * smoothSpeed);
					flag = Mathf.Abs(sliderProgress.value - targetProgress) < 0.01f;
				}
				if (fillImage != null)
				{
					float num = targetProgress / maxProgress;
					fillImage.fillAmount = Mathf.Lerp(fillImage.fillAmount, num, Time.deltaTime * smoothSpeed);
					flag2 = Mathf.Abs(fillImage.fillAmount - num) < 0.001f;
				}
				UpdateProgressText();
				this.OnProgressChanged?.Invoke(currentProgress);
				CheckZeroAndUpdateFill();
				if (flag && flag2)
				{
					break;
				}
				yield return null;
			}
			ApplyProgressImmediate();
			smoothRoutine = null;
		}

		private void ApplyProgressImmediate()
		{
			if (sliderProgress != null)
			{
				sliderProgress.value = targetProgress;
			}
			if (fillImage != null)
			{
				fillImage.fillAmount = targetProgress / maxProgress;
			}
			UpdateProgressText();
			this.OnProgressChanged?.Invoke(currentProgress);
			CheckZeroAndUpdateFill();
			if (IsComplete())
			{
				this.OnProgressCompleted?.Invoke();
			}
		}

		private void UpdateProgressText()
		{
			if (progressText != null)
			{
				float f = currentProgress / maxProgress * 100f;
				string translation = LocalizationMediator.GetTranslation("ACH_InfoPanel_Archievement_Progression_Complete");
				string text = (string.IsNullOrEmpty(translation) ? "Complete" : translation);
				string text2 = $"<color=blue>{Mathf.RoundToInt(f)}%</color> <color=lightblue>{text}\n{Mathf.RoundToInt(currentProgress)}/{Mathf.RoundToInt(maxProgress)}</color>";
				progressText.text = text2;
			}
		}

		public void ResetProgress()
		{
			SetProgress(0f);
		}

		public bool IsComplete()
		{
			return currentProgress >= maxProgress;
		}
	}
}
