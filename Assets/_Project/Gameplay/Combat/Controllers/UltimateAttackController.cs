using AstralShift;
using AstralShift.Control;
using AstralShift.Control.Controllers;
using AstralShift.FSM;
using AstralShift.HellMaiden;
using AstralShift.HellMaiden.Audio;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.Helpers;
using AstralShift.Managers;
using AstralShift.Rendering;
using Rewired;
using UnityEngine;

namespace Assets.Scripts.AstralShift.HellMaiden.Controllers
{
	public class UltimateAttackController : UIController
	{
		private StateMachine stateMachine;

		private State Idle;

		private State SplashScreen;

		private State Animation;

		private State UltimateAttack;

		private UltimateAttackManager ultimateAttackManager;

		private bool _skipInSplashScreen;

		private bool _canSkipAnimation;

		[SerializeField]
		private float skipHoldTime = 1f;

		public void Init(UltimateAttackManager manager)
		{
			ultimateAttackManager = GameDirector.Instance.Player.ultimateAttackManager;
			ultimateAttackManager = manager;
			ultimateAttackManager.UltimateAttackEvents.onSplashTransitionStart = OnSplashScreenTransition;
			ultimateAttackManager.UltimateAttackEvents.onSplashEnd = OnSplashScreenEnd;
			ultimateAttackManager.UltimateAttackEvents.onAnimationSkipPointReached = OnAnimationSkipPointReached;
			ultimateAttackManager.UltimateAttackEvents.onAnimationTransparencyPointReached = OnAnimationTransparencyPointReached;
			ultimateAttackManager.UltimateAttackEvents.SetSkipHoldTime(skipHoldTime);
			stateMachine = new StateMachine("UltimateAttackFSM");
			Idle = new State("Idle");
			SplashScreen = new State("SplashScreen");
			Animation = new State("Animation");
			UltimateAttack = new State("UltimateAttack");
			stateMachine.AddAnyTransition(SplashScreen);
			stateMachine.AddTransition(SplashScreen, Animation);
			stateMachine.AddTransition(SplashScreen, UltimateAttack);
			stateMachine.AddTransition(Animation, UltimateAttack);
			stateMachine.AddTransition(UltimateAttack, Idle);
			stateMachine.SetInitialState(Idle);
			SplashScreen.onEnter = SplashScreenEnter;
			Animation.onEnter = AnimationEnter;
			UltimateAttack.onEnter = UltimateAttackEnter;
		}

		public override void Activate()
		{
			InputHandler.EnableMenuInputs();
			ASRendererFeature.Instance.EnableFullscreenBlurRenderPass(enable: false);
			MusicPlayer.Instance.SetSnapShot(MusicPlayer.SnapshotID.Ultimate);
			stateMachine.MakeTransition(SplashScreen);
			GameDirector.Instance.Player.SetInvulnerable(state: true);
			_skipInSplashScreen = GameDirector.Instance.Settings.UltiSkip;
			_canSkipAnimation = false;
		}

		public override void Deactivate()
		{
			base.Deactivate();
			GameDirector.Instance.Player.SetInvulnerable(state: false);
			_canSkipAnimation = false;
		}

		public void SplashScreenEnter()
		{
			PauseManager.Instance.PauseGame();
			ultimateAttackManager.UltimateAttackEvents.StartSplashScreen();
		}

		public void OnSplashScreenTransition()
		{
			if (!_skipInSplashScreen)
			{
				float fadeInOutTime = ultimateAttackManager.UltimateAttackEvents.FadeInOutTime;
				FadeOut(fadeInOutTime);
			}
		}

		public void OnSplashScreenEnd()
		{
			if (_skipInSplashScreen)
			{
				PauseManager.Instance.ResumeGame();
				ultimateAttackManager.UltimateAttackEvents.StopSounds();
				stateMachine.MakeTransition(UltimateAttack);
			}
			else
			{
				stateMachine.MakeTransition(Animation);
			}
		}

		public async void AnimationEnter()
		{
			while (!ultimateAttackManager.UltimateAttackEvents.AnimationVideo.IsPreWarmed)
			{
				await Awaitable.NextFrameAsync();
			}
			_canSkipAnimation = true;
			ultimateAttackManager.UltimateAttackEvents.StartAnimationVideo();
			ultimateAttackManager.UltimateAttackEvents.SetSkipHoldTime(skipHoldTime);
			float fadeInOutTime = ultimateAttackManager.UltimateAttackEvents.FadeInOutTime;
			FadeIn(fadeInOutTime);
		}

		public void OnAnimationSkipPointReached()
		{
			_canSkipAnimation = false;
		}

		public void OnAnimationTransparencyPointReached()
		{
			PauseManager.Instance.ResumeGame();
			stateMachine.MakeTransition(UltimateAttack);
		}

		public void OnAnimationEnd()
		{
			stateMachine.MakeTransition(UltimateAttack);
		}

		public void UltimateAttackEnter()
		{
			ControllerManager.Instance.YieldGameController();
			stateMachine.MakeTransition(Idle);
			ultimateAttackManager.UltimateAttackWeaponBehaviour.Attack();
		}

		public void FadeOut(float fadeTime)
		{
			ScreenFader.Instance.FadeOut(fadeTime);
		}

		public void FadeIn(float fadeTime)
		{
			ScreenFader.Instance.FadeIn(fadeTime);
		}

		public override void UICancelReleased(InputActionEventData data)
		{
			TimerHoldInteractionTaskHelper.CancelAndDispose();
		}

		public override void UICancelHeld(InputActionEventData data)
		{
			if (_canSkipAnimation)
			{
				TimerHoldInteractionTaskHelper.ProcessHoldAsync(skipHoldTime, delegate
				{
					_canSkipAnimation = false;
					ultimateAttackManager.UltimateAttackEvents.SkipAnimationVideo();
				});
			}
		}
	}
}
