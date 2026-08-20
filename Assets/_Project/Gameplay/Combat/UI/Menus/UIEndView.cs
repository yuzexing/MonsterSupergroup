using System.Collections;
using System.Collections.Generic;
using Animancer;
using AstralShift.HellMaiden.Audio;
using AstralShift.HellMaiden.Controllers;
using AstralShift.Managers;
using Coffee.UIExtensions;
using Cysharp.Threading.Tasks;
using FMOD.Studio;
using FMODUnity;
using TMPro;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Menus
{
	public class UIEndView : MonoBehaviour
	{
		[SerializeField]
		private CanvasGroup parentGroup;

		[SerializeField]
		private CanvasGroup deathCanvasGroup;

		[SerializeField]
		private CanvasGroup winCanvasGroup;

		[Header("Death")]
		[SerializeField]
		private AnimancerComponent deathAnimancer;

		[SerializeField]
		private ClipTransition deathOpenAnimation;

		[SerializeField]
		private ClipTransition deathIdleAnimation;

		[SerializeField]
		private ClipTransition deathCloseAnimation;

		[SerializeField]
		private RunStatsPanel deathRunStatsPanel;

		[Header("Win")]
		[SerializeField]
		private AnimancerComponent winAnimancer;

		[SerializeField]
		private ClipTransition winOpenAnimation;

		[SerializeField]
		private ClipTransition winIdleAnimation;

		[SerializeField]
		private ClipTransition winCloseAnimation;

		[SerializeField]
		private RunStatsPanel winRunStatsPanel;

		[SerializeField]
		private UIParticle confettiParticle;

		[Header("Sound")]
		[SerializeField]
		private EventReference defeatBGM;

		[SerializeField]
		private EventReference winBGM;

		[SerializeField]
		private EventReference defeatEnterSound;

		[SerializeField]
		private EventReference defeatExitSound;

		[SerializeField]
		private EventReference winLoopSound;

		[SerializeField]
		private EventReference winConfettiSound;

		[SerializeField]
		private float confettiLoopTime = 5f;

		[Header("Defeat Quotes")]
		[SerializeField]
		private List<string> defeatQuotes;

		[SerializeField]
		private TextMeshProUGUI defeatQuoteText;

		[Header("Win Quotes")]
		[SerializeField]
		private List<string> winQuotes;

		[SerializeField]
		private TextMeshProUGUI winQuoteText;

		private EndScreenController _controller;

		private CanvasGroup _activeCanvasGroup;

		private RunStatsPanel _statsPanelToActivate;

		private AnimancerComponent _animancer;

		private ClipTransition _openAnimation;

		private ClipTransition _idleAnimation;

		private ClipTransition _closeAnimation;

		private EventInstance _winLoopSoundInstance;

		private AnimancerState _openCloseAnimationState;

		private bool skiped;

		private Coroutine confettiLoopCoroutine;

		public void Init()
		{
			parentGroup.interactable = false;
			parentGroup.blocksRaycasts = false;
			parentGroup.alpha = 0f;
			deathCanvasGroup.gameObject.SetActive(value: false);
			winCanvasGroup.gameObject.SetActive(value: false);
			winRunStatsPanel.gameObject.SetActive(value: true);
			deathRunStatsPanel.gameObject.SetActive(value: true);
		}

		public void OpenDeathScreen()
		{
			_animancer = deathAnimancer;
			_openAnimation = deathOpenAnimation;
			_closeAnimation = deathCloseAnimation;
			_idleAnimation = deathIdleAnimation;
			_activeCanvasGroup = deathCanvasGroup;
			_statsPanelToActivate = deathRunStatsPanel;
			string term = defeatQuotes[Random.Range(0, defeatQuotes.Count)];
			LocalizationMediator.GetTranslation(ref term);
			defeatQuoteText.SetText(term);
			if (!defeatBGM.IsNull)
			{
				MusicPlayer.Instance.QueueMusic(defeatBGM.Guid);
				MusicPlayer.Instance.PlayNextMusic();
			}
			Open();
		}

		public void OpenWinScreen()
		{
			_animancer = winAnimancer;
			_openAnimation = winOpenAnimation;
			_closeAnimation = winCloseAnimation;
			_idleAnimation = winIdleAnimation;
			_activeCanvasGroup = winCanvasGroup;
			_statsPanelToActivate = winRunStatsPanel;
			string term = winQuotes[Random.Range(0, winQuotes.Count)];
			LocalizationMediator.GetTranslation(ref term);
			winQuoteText.SetText(term);
			if (!winBGM.IsNull)
			{
				MusicPlayer.Instance.QueueMusic(winBGM.Guid);
				MusicPlayer.Instance.PlayNextMusic();
			}
			if (!winLoopSound.IsNull)
			{
				_winLoopSoundInstance = RuntimeManager.CreateInstance(winLoopSound.Guid);
				_winLoopSoundInstance.start();
			}
			confettiLoopCoroutine = StartCoroutine(ConfettiLoop());
			Open().Forget();
		}

		private async UniTask Open()
		{
			_controller = ControllerManager.Instance.OverrideGameController<EndScreenController>();
			parentGroup.interactable = false;
			parentGroup.blocksRaycasts = false;
			parentGroup.alpha = 1f;
			_activeCanvasGroup.gameObject.SetActive(value: true);
			_controller.OnCenter2Pressed += SkipMenu;
			_controller.OnUISubmitPressed += SkipMenu;
			await OpenAnimation();
			_controller.OnCenter2Pressed -= SkipMenu;
			_controller.OnUISubmitPressed -= SkipMenu;
			parentGroup.interactable = true;
			parentGroup.blocksRaycasts = true;
			if (skiped)
			{
				Close();
			}
			else
			{
				_controller.OnAnyInputDown += Close;
			}
		}

		private async UniTask OpenAnimation()
		{
			await Awaitable.EndOfFrameAsync();
			_openCloseAnimationState = _animancer.Layers[0].Play(_openAnimation, _openAnimation.FadeDuration);
			while (_openCloseAnimationState.IsPlayingAndNotEnding())
			{
				await Awaitable.NextFrameAsync();
			}
			_animancer.Layers[0].Play(_idleAnimation, _idleAnimation.FadeDuration);
		}

		private async UniTask CloseAnimation()
		{
			await Awaitable.EndOfFrameAsync();
			_openCloseAnimationState = _animancer.Layers[0].Play(_closeAnimation, _closeAnimation.FadeDuration);
			while (_openCloseAnimationState.IsPlayingAndNotEnding())
			{
				await Awaitable.NextFrameAsync();
			}
		}

		private void SkipMenu()
		{
			_animancer.Layers[0].Speed = 3f;
			skiped = true;
		}

		public async void Close()
		{
			_controller.OnAnyInputDown -= Close;
			await CloseAnimation();
			if (_winLoopSoundInstance.isValid())
			{
				_winLoopSoundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
				_winLoopSoundInstance.release();
			}
			if (confettiLoopCoroutine != null)
			{
				StopCoroutine(confettiLoopCoroutine);
			}
			_animancer.gameObject.SetActive(value: false);
			_statsPanelToActivate.Open();
		}

		public void PlayDefeatEnterSound()
		{
			if (!defeatEnterSound.IsNull)
			{
				RuntimeManager.PlayOneShot(defeatEnterSound.Guid);
			}
		}

		public void PlayExitSound()
		{
			if (!defeatExitSound.IsNull)
			{
				RuntimeManager.PlayOneShot(defeatExitSound.Guid);
			}
		}

		public void PlayConfettiSound()
		{
			if (!winConfettiSound.IsNull)
			{
				RuntimeManager.PlayOneShot(winConfettiSound.Guid);
			}
		}

		private IEnumerator ConfettiLoop()
		{
			while (true)
			{
				confettiParticle.Play();
				PlayConfettiSound();
				yield return new WaitForSecondsRealtime(confettiLoopTime);
			}
		}
	}
}
