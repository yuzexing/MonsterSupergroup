using System;
using AstralShift.HellMaiden.Scenes;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Audio
{
	public class MusicPlayer : MonoBehaviour
	{
		public enum SnapshotID
		{
			Menu = 0,
			Normal = 1,
			Card = 2,
			Ultimate = 3,
			Dialogue = 4
		}

		private EventInstance currentMusic;

		private GUID currentMusicEvent;

		private int savedTimelinePosition;

		private EventInstance overridenMusic;

		private GUID overrideMusicEvent;

		private bool currentlyOverridingMusic;

		private EventInstance gameStatus;

		public HighLevelEmitterManager highLevelEmitterManager;

		[SerializeField]
		private MusicTrackLUT musicTrackLUT;

		private GUID _nextMusicEvent;

		public EventInstance CurrentMusicEvent
		{
			get
			{
				if (currentlyOverridingMusic)
				{
					return overridenMusic;
				}
				return currentMusic;
			}
		}

		public static MusicPlayer Instance { get; private set; }

		public event Action onNextTrack;

		public void Init()
		{
			Instance = this;
			SceneMaster.Instance.OnSceneInitPersist += delegate
			{
				PlayNextMusic();
			};
			gameStatus = RuntimeManager.CreateInstance("event:/snapshots/game_status");
			gameStatus.start();
		}

		public void QueueMusic(GUID nextMusicEvent)
		{
			_nextMusicEvent = nextMusicEvent;
		}

		public void PlayNextMusic(bool stopImmediately = false)
		{
			if (_nextMusicEvent == currentMusicEvent)
			{
				return;
			}
			if (currentMusic.isValid())
			{
				currentMusic.stop(stopImmediately ? FMOD.Studio.STOP_MODE.IMMEDIATE : FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
				currentMusic.release();
			}
			if (_nextMusicEvent.IsNull)
			{
				currentMusicEvent = default(GUID);
				return;
			}
			try
			{
				currentMusic = RuntimeManager.CreateInstance(_nextMusicEvent);
				currentMusicEvent = _nextMusicEvent;
				if (!currentlyOverridingMusic)
				{
					currentMusic.start();
					this.onNextTrack?.Invoke();
				}
				else
				{
					savedTimelinePosition = 0;
				}
			}
			catch (EventNotFoundException ex)
			{
				UnityEngine.Debug.LogWarning(ex.Message, this);
			}
		}

		public void PlayOverridenMusic(GUID musicEvent, bool swapImmediate = false)
		{
			if (overrideMusicEvent == musicEvent)
			{
				GUID gUID = overrideMusicEvent;
				UnityEngine.Debug.Log("Can't override same music event: " + gUID.ToString());
				return;
			}
			if (overridenMusic.isValid())
			{
				overridenMusic.stop(swapImmediate ? FMOD.Studio.STOP_MODE.IMMEDIATE : FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
				overridenMusic.release();
			}
			if (!musicEvent.IsNull)
			{
				try
				{
					overridenMusic = RuntimeManager.CreateInstance(musicEvent);
					currentMusic.getTimelinePosition(out savedTimelinePosition);
					currentMusic.stop(swapImmediate ? FMOD.Studio.STOP_MODE.IMMEDIATE : FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
					currentlyOverridingMusic = true;
					overridenMusic.start();
					this.onNextTrack?.Invoke();
				}
				catch (EventNotFoundException ex)
				{
					UnityEngine.Debug.LogWarning(ex.Message, this);
				}
			}
			else
			{
				currentMusic.getTimelinePosition(out savedTimelinePosition);
				currentMusic.stop(swapImmediate ? FMOD.Studio.STOP_MODE.IMMEDIATE : FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
				currentlyOverridingMusic = true;
			}
			overrideMusicEvent = musicEvent;
		}

		public void PlayOverridenMusic(MusicTrack track, bool swapImmediate = false)
		{
			EventReference eventReference;
			if (musicTrackLUT == null)
			{
				UnityEngine.Debug.LogWarning("MusicPlayer: MusicTrackLUT not assigned.", this);
			}
			else if (musicTrackLUT.TryGetEvent(track, out eventReference))
			{
				PlayOverridenMusic(eventReference.Guid, swapImmediate);
			}
			else
			{
				UnityEngine.Debug.LogWarning($"MusicPlayer: no FMOD event mapped for MusicTrack.{track}", this);
			}
		}

		public void StopCurrentOverridenMusic(bool swapImmediate = false)
		{
			if (overridenMusic.isValid() && currentlyOverridingMusic)
			{
				overridenMusic.stop(swapImmediate ? FMOD.Studio.STOP_MODE.IMMEDIATE : FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
				overridenMusic.release();
				currentMusic.setTimelinePosition(savedTimelinePosition);
				currentlyOverridingMusic = false;
				currentMusic.start();
			}
			else if (currentlyOverridingMusic)
			{
				currentMusic.setTimelinePosition(savedTimelinePosition);
				currentlyOverridingMusic = false;
				currentMusic.start();
			}
			overrideMusicEvent = default(GUID);
		}

		public void PauseMusic(bool pauseState)
		{
			if (currentMusic.isValid())
			{
				currentMusic.setPaused(pauseState);
			}
		}

		public void SetParameter(string name, int value)
		{
			if (overridenMusic.isValid())
			{
				overridenMusic.setParameterByName(name, value);
			}
			else if (currentMusic.isValid())
			{
				currentMusic.setParameterByName(name, value);
			}
		}

		public void StopAllMusicImmediately()
		{
			if (overridenMusic.isValid())
			{
				overridenMusic.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
				currentlyOverridingMusic = false;
			}
			if (currentMusic.isValid())
			{
				currentMusic.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
			}
			if (!currentMusicEvent.IsNull)
			{
				currentMusicEvent = default(GUID);
			}
		}

		public void StopAllMusic()
		{
			if (overridenMusic.isValid())
			{
				overridenMusic.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
				overrideMusicEvent = default(GUID);
				currentlyOverridingMusic = false;
			}
			if (currentMusic.isValid())
			{
				currentMusic.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
				currentMusicEvent = default(GUID);
			}
			if (!currentMusicEvent.IsNull)
			{
				currentMusicEvent = default(GUID);
			}
		}

		public void SetSnapShot(SnapshotID id)
		{
			RuntimeManager.StudioSystem.setParameterByName("GameStatus", (float)id);
		}
	}
}
