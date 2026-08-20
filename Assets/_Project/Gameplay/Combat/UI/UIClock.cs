using Animancer;
using AstralShift.Helpers.Attributes;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.UI
{
	public class UIClock : MonoBehaviour
	{
		[SerializeField]
		private UIClockNumbers minutesTextFirstDigit;

		[SerializeField]
		private UIClockNumbers minutesTextSecondDigit;

		[SerializeField]
		private UIClockNumbers secondsTextFirstDigit;

		[SerializeField]
		private UIClockNumbers secondsTextSecondDigit;

		[SerializeField]
		private float timeoutWarning = 11f;

		[SerializeField]
		private EventReference tickSound;

		[SerializeField]
		private EventReference lastSecTickSound;

		[SerializeField]
		private int lastSecondsStartSound = 10;

		private int lastTickSecond = -1;

		[SerializeField]
		private EventReference alertSound;

		[SerializeField]
		private EventReference startSound;

		[SerializeField]
		private EventReference timeoutSound;

		private bool timeoutPlayed;

		private bool alertPlayed;

		private bool skipWarningNextCall;

		[ReadOnly]
		public float seconds;

		[SerializeField]
		private bool animate = true;

		[SerializeField]
		private bool sideQuestFlag;

		[SerializeField]
		private AnimancerComponent animancerComponent;

		[SerializeField]
		private ClipTransition countdownAnimation;

		public void Deactivate()
		{
			base.enabled = false;
			animancerComponent.Layers[0].Stop();
			skipWarningNextCall = false;
		}

		public void Activate()
		{
			base.enabled = true;
			lastTickSecond = -1;
			alertPlayed = false;
			skipWarningNextCall = true;
			timeoutPlayed = false;
			minutesTextSecondDigit.ChangeNumber(0, animate: false);
			minutesTextFirstDigit.ChangeNumber(0, animate: false);
			secondsTextSecondDigit.ChangeNumber(0, animate: false);
			secondsTextFirstDigit.ChangeNumber(0, animate: false);
			RuntimeManager.PlayOneShot(startSound);
		}

		public void SetValue(float totalSeconds)
		{
			if (!base.enabled)
			{
				return;
			}
			seconds = Mathf.Clamp(totalSeconds, 0f, float.MaxValue);
			int num = (int)seconds;
			int num2 = num / 60;
			int num3 = num % 60;
			bool num4 = num != lastTickSecond;
			lastTickSecond = num;
			minutesTextSecondDigit.ChangeNumber(num2 / 10, animate);
			minutesTextFirstDigit.ChangeNumber(num2 % 10, animate);
			secondsTextSecondDigit.ChangeNumber(num3 / 10, animate);
			secondsTextFirstDigit.ChangeNumber(num3 % 10, animate);
			if (num4)
			{
				if (num <= lastSecondsStartSound)
				{
					PlayLastSecondsTick(num);
				}
				else if (!tickSound.IsNull)
				{
					RuntimeManager.PlayOneShot(tickSound);
				}
			}
			if (totalSeconds <= timeoutWarning && sideQuestFlag)
			{
				if (skipWarningNextCall && totalSeconds <= 0f)
				{
					skipWarningNextCall = false;
					return;
				}
				CountDownElement();
				if (!alertPlayed)
				{
					alertPlayed = true;
					RuntimeManager.PlayOneShot(alertSound);
				}
			}
			if (seconds <= 0f && !timeoutPlayed)
			{
				timeoutPlayed = true;
				RuntimeManager.PlayOneShot(timeoutSound);
			}
			skipWarningNextCall = false;
		}

		public void StartCountdown(bool runup)
		{
			minutesTextFirstDigit.Runup = runup;
			minutesTextSecondDigit.Runup = runup;
			secondsTextFirstDigit.Runup = runup;
			secondsTextSecondDigit.Runup = runup;
		}

		private void CountDownElement()
		{
			animancerComponent.Layers[0].Play(countdownAnimation);
		}

		private void PlayLastSecondsTick(int secondsLeft)
		{
			if (!lastSecTickSound.IsNull)
			{
				EventInstance eventInstance = RuntimeManager.CreateInstance(lastSecTickSound);
				eventInstance.setParameterByName("quest_timer_frenzy", secondsLeft);
				eventInstance.start();
				eventInstance.release();
			}
		}
	}
}
