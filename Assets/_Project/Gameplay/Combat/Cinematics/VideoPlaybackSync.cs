using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Video;

namespace AstralShift.Cinematics
{
	public class VideoPlaybackSync : MonoBehaviour
	{
		[Serializable]
		public struct VideoEvent
		{
			public float timeStamp;

			public UnityEvent unityEvent;
		}

		public VideoPlayer videoPlayer;

		public bool playOnStart;

		public Action OnVideoStart;

		public Action OnVideoEnd;

		protected Action<VideoPlayer> OnVideoPreparedAction;

		public VideoEvent[] videoEvents;

		protected bool _videoIsPlaying;

		protected int _videoEventIdx;

		private long _frameCount;

		[SerializeField]
		[Tooltip("Attaches a Timeline and plays it at the same time as the video.")]
		protected bool hasTimeline;

		[SerializeField]
		protected PlayableDirector timeline;

		public virtual bool VideoIsPlaying => _videoIsPlaying;

		public int CurrentEventIndex => _videoEventIdx;

		protected virtual void Reset()
		{
			if (videoPlayer == null)
			{
				videoPlayer = GetComponent<VideoPlayer>();
			}
		}

		protected virtual void Start()
		{
			if (playOnStart)
			{
				Init();
				Play();
			}
		}

		public virtual void Init()
		{
			videoPlayer.prepareCompleted += delegate(VideoPlayer vp)
			{
				OnVideoPreparedAction?.Invoke(vp);
			};
			videoPlayer.Stop();
			videoPlayer.Prepare();
			if (hasTimeline)
			{
				timeline.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
				timeline.playOnAwake = false;
				timeline.Stop();
			}
		}

		public virtual void Play()
		{
			_videoEventIdx = 0;
			_frameCount = (long)videoPlayer.clip.frameCount;
			videoPlayer.Play();
			_videoIsPlaying = true;
			OnVideoStart?.Invoke();
			if (hasTimeline)
			{
				timeline.time = 0.0;
				timeline.Play();
			}
		}

		public virtual void SetPaused(bool paused)
		{
			if (paused)
			{
				videoPlayer.Pause();
				if (hasTimeline)
				{
					timeline.Pause();
				}
			}
			else
			{
				videoPlayer.Play();
				if (hasTimeline)
				{
					timeline.Resume();
				}
			}
		}

		protected virtual void LateUpdate()
		{
			if (_videoIsPlaying)
			{
				if (hasTimeline)
				{
					timeline.time = videoPlayer.time;
				}
				if (videoPlayer.frame >= _frameCount - 2)
				{
					EndVideo();
				}
				else
				{
					CheckVideoEvents(videoPlayer.time);
				}
			}
		}

		protected virtual void CheckVideoEvents(double seconds)
		{
			if (_videoEventIdx > videoEvents.Length - 1)
			{
				return;
			}
			for (int i = _videoEventIdx; i < videoEvents.Length; i++)
			{
				if ((double)videoEvents[_videoEventIdx].timeStamp <= seconds)
				{
					videoEvents[_videoEventIdx].unityEvent.Invoke();
					_videoEventIdx++;
				}
			}
		}

		public virtual void Skip(double seconds = -1.0)
		{
			if (seconds == -1.0)
			{
				if (hasTimeline)
				{
					timeline.time = timeline.duration;
				}
				EndVideo();
			}
			else
			{
				videoPlayer.time = seconds;
			}
		}

		protected virtual void EndVideo()
		{
			_videoIsPlaying = false;
			CheckVideoEvents(double.MaxValue);
			OnVideoEnd?.Invoke();
		}
	}
}
