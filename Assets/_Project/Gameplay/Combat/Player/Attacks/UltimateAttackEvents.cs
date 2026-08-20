using System;
using System.Collections;
using System.Collections.Generic;
using AstralShift.Cinematics;
using AstralShift.HellMaiden.Audio;
using AstralShift.Helpers;
using DG.Tweening;
using FMOD.Studio;
using FMODUnity;
// using PixelCrushers.DialogueSystem;
using UnityEngine;
using UnityEngine.Playables;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class UltimateAttackEvents : MonoBehaviour
	{
		[Serializable]
		public class TimedVoiceLine
		{
			[Tooltip("Time (in seconds) into the animation video at which this VA fires. 0 = as soon as the video starts.")]
			[Min(0f)]
			public float triggerTime;

			[Tooltip("One of these line IDs is picked at random.")]
			public List<string> lineIds = new List<string>();
		}

		public PlayableDirector SplashScreen;

		public CinematicPlayer AnimationVideo;

		[SerializeField]
		private CanvasGroup SplashScreenCanvasGroup;

		[SerializeField]
		private CanvasGroup AnimationVideoCanvasGroup;

		[SerializeField]
		private CanvasGroup[] blackBarsCanvasGroups;

		[SerializeField]
		private float blackBarsFadeTime = 0.5f;

		private Sequence _blackBarsFadeSequence;

		[SerializeField]
		protected float fadeInOutTime = 0.5f;

		public Action onSplashTransitionStart;

		public Action onSplashEnd;

		public Action onAnimationSkipPointReached;

		public Action onAnimationTransparencyPointReached;

		[SerializeField]
		private CustomUnityUIPlayerControllerElementGlyph skipGlyph;

		[Header("Sound")]
		[SerializeField]
		private EventReference ultimateSoundReference;

		[SerializeField]
		private EventReference ultimateBGMReference;

		private EventInstance ultimateBGMInstance;

		private EventInstance ultimateSoundInstance;

		private const string skipParameter = "Skip";

		[SerializeField]
		private string splashScreenVaId;

		[Tooltip("VAs fired at specific times during the animation video.")]
		[SerializeField]
		private List<TimedVoiceLine> timedVoiceLines = new List<TimedVoiceLine>();

		private Coroutine _voiceLineRoutine;

		protected string eventName = "event:/sx/dlg/sx_dlg_vo";

		public float FadeInOutTime => fadeInOutTime;

		private void Start()
		{
			SplashScreenCanvasGroup.alpha = 0f;
			AnimationVideoCanvasGroup.alpha = 0f;
			CanvasGroup[] array = blackBarsCanvasGroups;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].alpha = 0f;
			}
			SplashScreen.stopped += delegate
			{
				onSplashEnd?.Invoke();
				SplashScreenCanvasGroup.alpha = 0f;
			};
			if (!ultimateSoundReference.IsNull)
			{
				ultimateSoundInstance = RuntimeManager.CreateInstance(ultimateSoundReference);
			}
			if (!ultimateBGMReference.IsNull)
			{
				ultimateBGMInstance = RuntimeManager.CreateInstance(ultimateBGMReference);
			}
		}

		public void StartSplashScreen()
		{
			if (!ultimateSoundReference.IsNull)
			{
				ultimateSoundInstance.setParameterByName("Skip", 0f);
				ultimateSoundInstance.start();
			}
			if (!ultimateBGMReference.IsNull)
			{
				ultimateBGMInstance.setParameterByName("Skip", 0f);
				ultimateBGMInstance.start();
			}
			MusicPlayer.Instance.PauseMusic(pauseState: true);
			AnimationVideoCanvasGroup.alpha = 0f;
			AnimationVideo.SetOnVideoEndCallback(delegate
			{
				SplashScreenCanvasGroup.alpha = 0f;
				skipGlyph.gameObject.SetActive(value: false);
				StopVoiceLineWatcher();
				StopSounds();
				_blackBarsFadeSequence?.Kill();
				_blackBarsFadeSequence = DOTween.Sequence();
				CanvasGroup[] array = blackBarsCanvasGroups;
				foreach (CanvasGroup target in array)
				{
					_blackBarsFadeSequence.Join(target.DOFade(0f, blackBarsFadeTime));
				}
				_blackBarsFadeSequence.AppendCallback(delegate
				{
					AnimationVideoCanvasGroup.alpha = 0f;
				});
				_blackBarsFadeSequence.SetUpdate(isIndependentUpdate: true);
				_blackBarsFadeSequence.Restart();
			});
			AnimationVideo.PreWarm();
			SplashScreenCanvasGroup.alpha = 1f;
			SplashScreen.Play();
			// DialogueManager.instance.GetComponent<FmodProgramerEventPlayer>().PlayDialogue(eventName, splashScreenVaId);
		}

		public void StartSplashTransition()
		{
			onSplashTransitionStart?.Invoke();
		}

		public void StartAnimationVideo()
		{
			SplashScreenCanvasGroup.alpha = 0f;
			AnimationVideoCanvasGroup.alpha = 1f;
			CanvasGroup[] array = blackBarsCanvasGroups;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].alpha = 1f;
			}
			AnimationVideo.StartVideo();
			StartVoiceLineWatcher();
		}

		public void SetSkipHoldTime(float value)
		{
			skipGlyph.gameObject.SetActive(value: true);
			skipGlyph.SetHold(value);
		}

		public void SkipAnimationVideo()
		{
			StopVoiceLineWatcher();
			skipGlyph.gameObject.SetActive(value: false);
			AnimationVideo.SkipVideo();
			if (!ultimateSoundReference.IsNull)
			{
				ultimateSoundInstance.setParameterByName("Skip", 1f);
			}
			if (!ultimateBGMReference.IsNull)
			{
				ultimateBGMInstance.setParameterByName("Skip", 1f);
			}
		}

		public void StopSounds()
		{
			if (!ultimateSoundReference.IsNull)
			{
				ultimateSoundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
			}
			if (!ultimateBGMReference.IsNull)
			{
				ultimateBGMInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
			}
			MusicPlayer.Instance.PauseMusic(pauseState: false);
		}

		public void AnimationSkipReached()
		{
			onAnimationSkipPointReached?.Invoke();
		}

		public void AnimationTransparencyPointReached()
		{
			onAnimationTransparencyPointReached?.Invoke();
		}

		private void StartVoiceLineWatcher()
		{
			StopVoiceLineWatcher();
			if (timedVoiceLines.Count != 0)
			{
				_voiceLineRoutine = StartCoroutine(VoiceLineWatcher());
			}
		}

		private void StopVoiceLineWatcher()
		{
			if (_voiceLineRoutine != null)
			{
				StopCoroutine(_voiceLineRoutine);
				_voiceLineRoutine = null;
			}
		}

		private IEnumerator VoiceLineWatcher()
		{
			// FmodProgramerEventPlayer fmodPlayer = DialogueManager.instance.gameObject.GetComponent<FmodProgramerEventPlayer>();
			bool[] played = new bool[timedVoiceLines.Count];
			int remaining = timedVoiceLines.Count;
			while (remaining > 0)
			{
				double time = AnimationVideo.VideoPlayer.time;
				for (int i = 0; i < timedVoiceLines.Count; i++)
				{
					if (!played[i] && !(time < (double)timedVoiceLines[i].triggerTime))
					{
						played[i] = true;
						remaining--;
						// fmodPlayer.PlayRandomDialogueFromList(eventName, timedVoiceLines[i].lineIds, 1f);
					}
				}
				yield return null;
			}
			_voiceLineRoutine = null;
		}

		private void OnDestroy()
		{
			if (ultimateBGMInstance.isValid())
			{
				ultimateBGMInstance.release();
			}
			if (ultimateSoundInstance.isValid())
			{
				ultimateSoundInstance.release();
			}
		}
	}
}
