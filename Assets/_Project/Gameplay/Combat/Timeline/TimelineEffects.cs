using AstralShift.DebugTools;
using AstralShift.FadeEffect;
using AstralShift.HellMaiden.Audio;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.UI;
using Com.LuisPedroFonseca.ProCamera2D;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Timeline
{
	public class TimelineEffects : MonoBehaviour
	{
		private bool _playerSprites = true;

		private bool _playerLights = true;

		private bool _playerRender = true;

		public void ShakeCamera(int index)
		{
			CameraEffects.Instance.Shake(index);
		}

		public void ConstantShakeCamera(int index)
		{
			CameraEffects.Instance.ConstantShake(index);
		}

		public void StopConstantShaking()
		{
			CameraEffects.Instance.StopShake();
		}

		public void TogglePlayerSprite()
		{
			_playerSprites = !_playerSprites;
			if (_playerSprites)
			{
				GameDirector.Instance.Player.gameObject.GetComponent<CharacterInvisibility>().TurnOnSprite();
			}
			else
			{
				GameDirector.Instance.Player.gameObject.GetComponent<CharacterInvisibility>().TurnOffSprite();
			}
		}

		public void TogglePlayerLights()
		{
			_playerLights = !_playerLights;
			if (_playerLights)
			{
				GameDirector.Instance.Player.gameObject.GetComponent<CharacterInvisibility>().TurnOnLights();
			}
			else
			{
				GameDirector.Instance.Player.gameObject.GetComponent<CharacterInvisibility>().TurnOffLights();
			}
		}

		public void TogglePlayerRender()
		{
			_playerRender = !_playerRender;
			if (_playerRender)
			{
				GameDirector.Instance.Player.gameObject.GetComponent<CharacterInvisibility>().TurnOnRender();
			}
			else
			{
				GameDirector.Instance.Player.gameObject.GetComponent<CharacterInvisibility>().TurnOffRender();
			}
		}

		internal void ResetPlayerVisibility()
		{
			if (!_playerSprites)
			{
				TogglePlayerSprite();
			}
			if (!_playerLights)
			{
				TogglePlayerLights();
			}
			if (!_playerRender)
			{
				TogglePlayerRender();
			}
		}

		public void ToggleCameraBoundaries()
		{
			ProCamera2D.Instance.GetComponent<ProCamera2DNumericBoundaries>().UseNumericBoundaries = !ProCamera2D.Instance.GetComponent<ProCamera2DNumericBoundaries>().UseNumericBoundaries;
		}

		public void RemoveCameraFollowSmoothness(bool x)
		{
			if (x)
			{
				ChangeCameraFollowSmoothness(0f, ProCamera2D.Instance.VerticalFollowSmoothness);
			}
			else
			{
				ChangeCameraFollowSmoothness(ProCamera2D.Instance.HorizontalFollowSmoothness, 0f);
			}
		}

		public void RemoveFollowSmoothness()
		{
			ProCamera2D.Instance.HorizontalFollowSmoothness = 0f;
			ProCamera2D.Instance.VerticalFollowSmoothness = 0f;
		}

		public void ChangeCameraFollowSmoothness(float x, float y)
		{
			ProCamera2D.Instance.HorizontalFollowSmoothness = x;
			ProCamera2D.Instance.VerticalFollowSmoothness = y;
		}

		public void ChangeCameraHorizontalFollowSmoothness(float x)
		{
			ProCamera2D.Instance.HorizontalFollowSmoothness = x;
		}

		public void ChangeCameraVerticalFollowSmoothness(float y)
		{
			ProCamera2D.Instance.VerticalFollowSmoothness = y;
		}

		public void StopAllSounds()
		{
			DBL.Log(DBL.Module.Timeline, "Stopping all sounds");
		}

		public void StopAllMusicImmediately()
		{
			DBL.Log(DBL.Module.Timeline, "Stopping all music immediate");
		}

		public void StopAllMusic()
		{
			DBL.Log(DBL.Module.Timeline, "Stopping all music");
			MusicPlayer.Instance.StopAllMusic();
		}

		public void StopMusic()
		{
			DBL.Log(DBL.Module.Timeline, "Stopping music overriden");
			MusicPlayer.Instance.StopCurrentOverridenMusic();
		}

		public void PlayMusic(MusicTrack music, bool immediate = false)
		{
			DBL.Log(DBL.Module.Timeline, "\"PlayMusic: \" + track");
			MusicPlayer.Instance.PlayOverridenMusic(music, immediate);
		}

		public void PauseProgressionTimeline()
		{
			ProgressionManager.Instance.MainProgressionTimeline.PauseAllMilestones();
		}

		public void ResumeProgressionTimeline()
		{
			ProgressionManager.Instance.MainProgressionTimeline.ResumeAllMilestones();
		}

		public void DisableZoomToFit()
		{
			if (ProCamera2D.Instance.TryGetComponent<ProCamera2DZoomToFitTargets>(out var component))
			{
				component.enabled = false;
			}
		}

		public void EnableZoomToFit()
		{
			if (ProCamera2D.Instance.TryGetComponent<ProCamera2DZoomToFitTargets>(out var component))
			{
				component.enabled = true;
			}
		}

		public void CameraRetargetPlayer()
		{
			ProCamera2D.Instance.RemoveAllCameraTargets();
			ProCamera2D.Instance.AddCameraTarget(GameDirector.Instance.Player.transform);
		}

		public void VictoryScreen()
		{
			CombatUIManager.Instance.ShowWinScreen();
		}

		public void TutorialHUD()
		{
			TutorialManager.Instance.HUD.TryLaunchHUDTutorial(delegate
			{
				TutorialManager.Instance.Controls.TryLaunchControlsTutorial(null);
			}).Forget();
		}

		public void FadeOut(float duration)
		{
			ScreenFader instance = ScreenFader.Instance;
			if (instance.stateMachine.GetState() != instance.FadedIn)
			{
				instance.FadeIn(FadeEffectEnum.None, 0f);
			}
			instance.FadeOut(FadeEffectEnum.Default, duration);
		}

		public void FadeIn(float duration)
		{
			ScreenFader instance = ScreenFader.Instance;
			if (instance.stateMachine.GetState() != instance.FadedOut)
			{
				instance.FadeOut(FadeEffectEnum.None, 0f);
			}
			instance.FadeIn(FadeEffectEnum.Default, duration);
		}

		public void SetParameterBMG(string data)
		{
			string[] array = data.Split('|');
			if (array.Length == 2)
			{
				string arg = array[0];
				if (float.TryParse(array[1], out var result))
				{
					DBL.Log(DBL.Module.Timeline, $"Setted the current BGM paramenter {arg} to {result}");
					MusicPlayer.Instance.CurrentMusicEvent.setParameterByName(arg, result);
				}
			}
		}
	}
}
