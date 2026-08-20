using System;
using System.Threading;
using System.Threading.Tasks;
using AstralShift.FSM;
using AstralShift.FadeEffect;
using UnityEngine;

namespace AstralShift
{
	public class ScreenFader : MonoBehaviour
	{
		public static ScreenFader Instance;

		public FadeEffectData effectsData;

		[Header("Default Settings")]
		public FadeEffectEnum fadeOut;

		public FadeEffectEnum fadeIn;

		[SerializeField]
		private float fadeOutDuration = 1f;

		[SerializeField]
		private float fadeInDuration = 1f;

		private FadeEffectEnum _fadeOut;

		private FadeEffectEnum _fadeIn;

		private float _fadeOutDuration;

		private float _fadeInDuration;

		private BaseFadeEffect _lastFadeOut;

		private Task _currentFadeTask;

		private BaseFadeEffect[] _loadedEffects;

		private Action fadeOutCallback;

		private Action fadeInCallback;

		public StateMachine stateMachine;

		public State FadingOut;

		public State FadedOut;

		public State FadingIn;

		public State FadedIn;

		public void Awake()
		{
			Init();
		}

		private void Init()
		{
			Instance = this;
			InitStateMachine();
			_fadeOut = fadeOut;
			_fadeIn = fadeIn;
			_fadeOutDuration = fadeOutDuration;
			_fadeInDuration = fadeInDuration;
			_loadedEffects = new BaseFadeEffect[effectsData.effects.Count];
			for (int i = 0; i < effectsData.effects.Count; i++)
			{
				_loadedEffects[i] = UnityEngine.Object.Instantiate(effectsData.effects[i], base.transform);
				DeactivateEffect(_loadedEffects[i]);
			}
		}

		private void InitStateMachine()
		{
			stateMachine = new StateMachine("ScreenFader");
			FadingOut = new State("FadingOut");
			FadedOut = new State("FadedOut");
			FadingIn = new State("FadingIn");
			FadedIn = new State("FadedIn");
			stateMachine.AddTransition(FadedOut, FadingIn);
			stateMachine.AddTransition(FadingIn, FadedIn);
			stateMachine.AddTransition(FadedIn, FadingOut);
			stateMachine.AddTransition(FadingOut, FadedOut);
			stateMachine.SetInitialStateNoCallbacks(FadedIn);
		}

		public void SetFadeOut(FadeEffectEnum effect)
		{
			_fadeOut = effect;
		}

		public void SetFadeIn(FadeEffectEnum effect)
		{
			_fadeIn = effect;
		}

		public void SetFadeOut(FadeEffectEnum effect, float duration)
		{
			_fadeOut = effect;
			_fadeOutDuration = duration;
		}

		public void SetFadeIn(FadeEffectEnum effect, float duration)
		{
			_fadeIn = effect;
			_fadeInDuration = duration;
		}

		public void FadeOutCallback()
		{
			fadeOutCallback?.Invoke();
		}

		public void FadeInCallback()
		{
			fadeInCallback?.Invoke();
		}

		public void FadeOut(Action onEnd = null)
		{
			FadeOut(_fadeOut, _fadeOutDuration, onEnd);
		}

		public void FadeOut(float duration, Action onEnd = null)
		{
			FadeOut(_fadeOut, duration, onEnd);
		}

		public void FadeOut(FadeEffectEnum effect, float duration, Action onEnd = null)
		{
			CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
			FadeOutTask(cancellationTokenSource.Token, effect, duration, onEnd);
		}

		public async Task FadeOutTask(CancellationToken token, Action onEnd = null)
		{
			await FadeOutTask(token, _fadeOut, _fadeOutDuration, onEnd);
		}

		public async Task FadeOutTask(CancellationToken token, float duration, Action onEnd = null)
		{
			await FadeOutTask(token, _fadeOut, duration, onEnd);
		}

		public async Task FadeOutTask(CancellationToken token, FadeEffectEnum effect, float duration, Action onEnd = null)
		{
			if (stateMachine.GetState() == FadingIn)
			{
				await _currentFadeTask;
			}
			if (stateMachine.GetState() == FadingOut)
			{
				State fadedOut = FadedOut;
				fadedOut.onEnterOnce = (Action)Delegate.Combine(fadedOut.onEnterOnce, onEnd);
				Debug.LogWarning("SceneFader: FadeOut Called when already fading, will disregard and complete current fade and call OnEnd Callback anyway.");
				await _currentFadeTask;
				return;
			}
			if (stateMachine.GetState() == FadedOut)
			{
				onEnd?.Invoke();
				Debug.LogWarning("SceneFader: FadeOut Called when already faded, will disregard and call OnEnd Callback anyway.");
				return;
			}
			stateMachine.MakeTransition(FadingOut);
			int num = effectsData.enumFields.IndexOf(effect);
			ActivateEffect(_loadedEffects[num]);
			_lastFadeOut = _loadedEffects[num];
			State fadedOut2 = FadedOut;
			fadedOut2.onEnterOnce = (Action)Delegate.Combine(fadedOut2.onEnterOnce, onEnd);
			_currentFadeTask = _loadedEffects[num].FadeOutTask(token, duration, delegate
			{
				stateMachine.MakeTransition(FadedOut);
			});
			await _currentFadeTask;
		}

		public void FadeIn(Action onEnd = null)
		{
			FadeIn(_fadeIn, _fadeInDuration, onEnd);
		}

		public void FadeIn(float duration, Action onEnd = null)
		{
			FadeIn(_fadeIn, duration, onEnd);
		}

		public void FadeIn(FadeEffectEnum effect, float duration = 1f, Action onEnd = null)
		{
			CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
			FadeInTask(cancellationTokenSource.Token, effect, duration, onEnd);
		}

		public async Task FadeInTask(CancellationToken token, Action onEnd = null)
		{
			await FadeInTask(token, _fadeIn, _fadeInDuration, onEnd);
		}

		public async Task FadeInTask(CancellationToken token, float duration, Action onEnd = null)
		{
			await FadeInTask(token, _fadeIn, duration, onEnd);
		}

		public async Task FadeInTask(CancellationToken token, FadeEffectEnum effect, float duration = 1f, Action onEnd = null)
		{
			if (stateMachine.GetState() == FadingOut)
			{
				await _currentFadeTask;
			}
			if (stateMachine.GetState() == FadingIn)
			{
				State fadedIn = FadedIn;
				fadedIn.onEnterOnce = (Action)Delegate.Combine(fadedIn.onEnterOnce, onEnd);
				Debug.LogWarning("SceneFader: FadeIn Called when already fading, will disregard and complete current fade and call OnEnd Callback anyway.");
				await _currentFadeTask;
				return;
			}
			if (stateMachine.GetState() == FadedIn)
			{
				onEnd?.Invoke();
				Debug.LogWarning("SceneFader: FadeIn Called when already faded, will disregard and call OnEnd Callback anyway.");
				return;
			}
			DeactivateEffect(_lastFadeOut);
			stateMachine.MakeTransition(FadingIn);
			int index = effectsData.enumFields.IndexOf(effect);
			ActivateEffect(_loadedEffects[index]);
			State fadedIn2 = FadedIn;
			fadedIn2.onEnterOnce = (Action)Delegate.Combine(fadedIn2.onEnterOnce, (Action)delegate
			{
				DeactivateEffect(_loadedEffects[index]);
			});
			State fadedIn3 = FadedIn;
			fadedIn3.onEnterOnce = (Action)Delegate.Combine(fadedIn3.onEnterOnce, onEnd);
			_currentFadeTask = _loadedEffects[index].FadeInTask(token, duration, delegate
			{
				stateMachine.MakeTransition(FadedIn);
			});
			SetFadeOut(FadeEffectEnum.Default);
			SetFadeIn(FadeEffectEnum.Default);
			await _currentFadeTask;
		}

		private void ActivateEffect(BaseFadeEffect effect)
		{
			effect.gameObject.SetActive(value: true);
		}

		private void DeactivateEffect(BaseFadeEffect effect)
		{
			effect.gameObject.SetActive(value: false);
		}
	}
}
