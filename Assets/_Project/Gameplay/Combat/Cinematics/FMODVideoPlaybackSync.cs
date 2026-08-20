using AstralShift.HellMaiden.Audio;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Video;

namespace AstralShift.Cinematics
{
	[RequireComponent(typeof(VideoPlayer))]
	public class FMODVideoPlaybackSync : VideoPlaybackSync
	{
		public EventReference videoSound;

		private EventInstance _audioEvent;

		private double _timer;

		private double _videoLength;

		private float _frameRate = 24f;

		private long _setVideoFrame;

		private double _audioTimeInSeconds;

		private long _audioFrame;

		private long _frameDifference;

		private const int FRAME_TOLERANCE = 2;

		public double Timer => _timer;

		public double VideoLength => _videoLength;

		public float FrameRate => _frameRate;

		public double AudioTimeInSeconds => _audioTimeInSeconds;

		public long AudioFrame => _audioFrame;

		public long FrameDifference => _frameDifference;

		public override void Init()
		{
			_frameRate = videoPlayer.frameRate;
			_setVideoFrame = 0L;
			_videoLength = videoPlayer.length;
			videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
			videoPlayer.prepareCompleted += delegate(VideoPlayer vp)
			{
				OnVideoPreparedAction?.Invoke(vp);
			};
			if (!videoPlayer.isPrepared)
			{
				videoPlayer.Prepare();
			}
			if (hasTimeline)
			{
				timeline.timeUpdateMode = DirectorUpdateMode.Manual;
				timeline.playOnAwake = false;
				timeline.Stop();
			}
		}

		public override void Play()
		{
			MusicPlayer.Instance.PauseMusic(pauseState: true);
			_audioEvent = RuntimeManager.CreateInstance(videoSound);
			_audioTimeInSeconds = 0.0;
			_setVideoFrame = 0L;
			_timer = 0.0;
			videoPlayer.frame = 0L;
			videoPlayer.time = 0.0;
			videoPlayer.Play();
			_audioEvent.start();
			OnVideoStart?.Invoke();
			_videoIsPlaying = true;
			if (hasTimeline)
			{
				timeline.initialTime = 0.0;
				timeline.Play();
			}
		}

		public override void SetPaused(bool paused)
		{
			_audioEvent.setPaused(paused);
			base.SetPaused(paused);
		}

		protected override void LateUpdate()
		{
			if (!_videoIsPlaying)
			{
				return;
			}
			if (!Application.isFocused)
			{
				_audioEvent.setPaused(paused: true);
				videoPlayer.frame = _audioFrame;
				_setVideoFrame = _audioFrame;
				_frameDifference = 0L;
				return;
			}
			_audioEvent.setPaused(paused: false);
			_audioEvent.getTimelinePosition(out var position);
			_audioTimeInSeconds = (float)position / 1000f;
			_timer = _audioTimeInSeconds;
			if (videoPlayer.frame > _setVideoFrame)
			{
				_audioFrame = (long)((double)_frameRate * _audioTimeInSeconds);
				_frameDifference = _audioFrame - videoPlayer.frame;
			}
			else
			{
				_frameDifference = 0L;
			}
			if (hasTimeline)
			{
				timeline.time = _audioTimeInSeconds;
				timeline.Evaluate();
			}
			if (_frameDifference > 2)
			{
				videoPlayer.frame = _audioFrame;
				_setVideoFrame = _audioFrame;
			}
			if (videoPlayer.frame >= (long)(videoPlayer.frameCount - 2))
			{
				EndVideo();
			}
			else
			{
				CheckVideoEvents(_audioTimeInSeconds);
			}
		}

		public override void Skip(double seconds = -1.0)
		{
			if (seconds == -1.0)
			{
				if (hasTimeline)
				{
					timeline.time = timeline.duration;
					timeline.Evaluate();
				}
				EndVideo();
			}
			else
			{
				int timelinePosition = (int)(seconds * 1000.0);
				_audioEvent.setTimelinePosition(timelinePosition);
				RuntimeManager.StudioSystem.flushCommands();
			}
		}

		protected override void EndVideo()
		{
			_videoIsPlaying = false;
			MusicPlayer.Instance.PauseMusic(pauseState: false);
			_audioEvent.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
			_audioEvent.release();
			RuntimeManager.StudioSystem.flushCommands();
			OnVideoEnd?.Invoke();
			CheckVideoEvents(9.223372036854776E+18);
		}

		public void SetParameter(PARAMETER_ID id, float value)
		{
			_audioEvent.setParameterByID(id, value);
		}

		private void OnDestroy()
		{
			_audioEvent.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
			_audioEvent.release();
		}
	}
}
