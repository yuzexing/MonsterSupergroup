using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;

namespace AstralShift.Managers
{
	public class PauseManager : MonoBehaviour
	{
		public struct SlowMotionEffect
		{
			public uint Id;

			public int priority;

			public float timeScale;
		}

		public static PauseManager Instance;

		[SerializeField]
		private float SlowMoTimeScale = 0.25f;

		public float previousTimeScale;

		public Dictionary<uint, SlowMotionEffect> ActiveSlowMotionDict = new Dictionary<uint, SlowMotionEffect>();

		public Action OnGamePause;

		public Action OnGameResume;

		public Action OnPausePausables;

		public Action OnResumePausables;

		private int pauseCounter;

		private float _previousTimeScale = 1f;

		public float localTimeScale = 1f;

		private uint slowMoCountId;

		public float SlowMoTimeScaleValue => SlowMoTimeScale;

		public bool IsPaused { get; private set; }

		public void Awake()
		{
			if (Instance == null)
			{
				Instance = this;
			}
			else
			{
				UnityEngine.Object.Destroy(this);
			}
		}

		public void Clear()
		{
			pauseCounter = 0;
			_previousTimeScale = 1f;
			OnGamePause = null;
			OnGameResume = null;
			OnPausePausables = null;
			OnResumePausables = null;
		}

		public void PauseGame()
		{
			Debug.Log("Game <color=pink>Paused</color>");
			if (pauseCounter == 0)
			{
				_previousTimeScale = Time.timeScale;
			}
			Time.timeScale = 0f;
			OnGamePause?.Invoke();
			pauseCounter++;
		}

		public void ResumeGame()
		{
			Debug.Log("Game <color=pink>Resumed</color>");
			pauseCounter--;
			if (pauseCounter == 0)
			{
				Time.timeScale = _previousTimeScale;
				OnGameResume?.Invoke();
			}
			else if (pauseCounter < 0)
			{
				Time.timeScale = _previousTimeScale;
				pauseCounter = 0;
				OnGameResume?.Invoke();
				Debug.LogError("Too many Resumes for the amount of Pauses!");
			}
		}

		public void PausePausables()
		{
			if (!IsPaused)
			{
				IsPaused = true;
				Debug.Log("<color=red>Pausables paused</color>");
				OnPausePausables?.Invoke();
			}
		}

		public void ResumePausables()
		{
			if (IsPaused)
			{
				IsPaused = false;
				Debug.Log("<color=red>Pausables UNpaused</color>");
				OnResumePausables?.Invoke();
			}
		}

		public IEnumerator StartSlowMo(float waitFor, float minSpeed, float decrement)
		{
			while (Time.timeScale > minSpeed)
			{
				Time.timeScale = Mathf.Clamp(Time.timeScale - decrement, 0f, float.PositiveInfinity);
				localTimeScale = Time.timeScale;
				yield return new WaitForSecondsRealtime(waitFor);
				waitFor *= waitFor;
			}
		}

		public void StartSlowMo(float finalSpeed, float duration, Ease easingFunction)
		{
			((Tween)DOTween.To(() => Time.timeScale, delegate(float x)
			{
				Time.timeScale = x;
			}, finalSpeed, duration).SetEase(easingFunction).SetUpdate(UpdateType.Late, isIndependentUpdate: true)).Play();
		}

		public uint StartSlowMo(bool immediate, float slowMoTimescale = -1f)
		{
			if (slowMoTimescale == -1f)
			{
				slowMoTimescale = SlowMoTimeScale;
			}
			slowMoCountId++;
			ActiveSlowMotionDict.Add(slowMoCountId, new SlowMotionEffect
			{
				timeScale = slowMoTimescale,
				Id = slowMoCountId
			});
			Debug.Log("Starting slow mo");
			if (immediate)
			{
				Time.timeScale = slowMoTimescale;
			}
			else
			{
				StartCoroutine(StartSlowMo(0.2f, slowMoTimescale, slowMoTimescale / 3f));
			}
			return slowMoCountId;
		}

		public void StopSlowMo(bool immediate, uint id)
		{
			if (!ActiveSlowMotionDict.Remove(id))
			{
				if (ActiveSlowMotionDict.Count == 0)
				{
					Time.timeScale = 1f;
				}
			}
			else
			{
				float timeScale = ((ActiveSlowMotionDict.Count == 0) ? 1f : ActiveSlowMotionDict.ElementAt(ActiveSlowMotionDict.Count - 1).Value.timeScale);
				Time.timeScale = timeScale;
				Debug.Log("Stopping slow mo, timescale reverted to " + Time.timeScale);
			}
		}

		public void SetTimeScale(float timeScale)
		{
			Time.timeScale = timeScale;
		}

		public void ResetTimeScale()
		{
			Time.timeScale = 1f;
			ActiveSlowMotionDict.Clear();
		}

		private void OnDestroy()
		{
			ResetTimeScale();
		}
	}
}
