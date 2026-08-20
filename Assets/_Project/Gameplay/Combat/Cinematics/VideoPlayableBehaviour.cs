using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Video;

namespace AstralShift.Cinematics
{
	public class VideoPlayableBehaviour : PlayableBehaviour
	{
		public VideoPlayer videoPlayer;

		public VideoClip videoClip;

		public bool mute;

		public bool loop = true;

		public double preloadTime = 0.3;

		public double clipInTime;

		private bool playedOnce;

		private bool preparing;

		public void PrepareVideo()
		{
			if (videoPlayer == null || videoClip == null)
			{
				return;
			}
			if (videoPlayer.clip != videoClip)
			{
				StopVideo();
			}
			if (videoPlayer.isPrepared || preparing)
			{
				return;
			}
			videoPlayer.source = VideoSource.VideoClip;
			videoPlayer.clip = videoClip;
			videoPlayer.playOnAwake = false;
			videoPlayer.waitForFirstFrame = true;
			videoPlayer.isLooping = loop;
			for (ushort num = 0; num < videoClip.audioTrackCount; num++)
			{
				if (videoPlayer.audioOutputMode == VideoAudioOutputMode.Direct)
				{
					videoPlayer.SetDirectAudioMute(num, mute || !Application.isPlaying);
				}
				else if (videoPlayer.audioOutputMode == VideoAudioOutputMode.AudioSource)
				{
					AudioSource targetAudioSource = videoPlayer.GetTargetAudioSource(num);
					if (targetAudioSource != null)
					{
						targetAudioSource.mute = mute || !Application.isPlaying;
					}
				}
			}
			videoPlayer.loopPointReached += LoopPointReached;
			videoPlayer.time = clipInTime;
			videoPlayer.Prepare();
			preparing = true;
		}

		private void LoopPointReached(VideoPlayer vp)
		{
			playedOnce = !loop;
		}

		public override void PrepareFrame(Playable playable, FrameData info)
		{
			if (!(videoPlayer == null) && !(videoClip == null))
			{
				videoPlayer.timeReference = (Application.isPlaying ? VideoTimeReference.ExternalTime : VideoTimeReference.Freerun);
				if (videoPlayer.isPlaying && Application.isPlaying)
				{
					videoPlayer.externalReferenceTime = playable.GetTime();
				}
				else if (!Application.isPlaying)
				{
					SyncVideoToPlayable(playable);
				}
			}
		}

		public override void OnBehaviourPlay(Playable playable, FrameData info)
		{
			if (!(videoPlayer == null) && !playedOnce)
			{
				PlayVideo();
				SyncVideoToPlayable(playable);
			}
		}

		public override void OnBehaviourPause(Playable playable, FrameData info)
		{
			if (!(videoPlayer == null))
			{
				if (Application.isPlaying)
				{
					PauseVideo();
				}
				else
				{
					StopVideo();
				}
			}
		}

		public override void ProcessFrame(Playable playable, FrameData info, object playerData)
		{
			if (videoPlayer == null || videoPlayer.clip == null)
			{
				return;
			}
			videoPlayer.targetCameraAlpha = info.weight;
			if (!Application.isPlaying)
			{
				return;
			}
			for (ushort num = 0; num < videoPlayer.clip.audioTrackCount; num++)
			{
				if (videoPlayer.audioOutputMode == VideoAudioOutputMode.Direct)
				{
					videoPlayer.SetDirectAudioVolume(num, info.weight);
				}
				else if (videoPlayer.audioOutputMode == VideoAudioOutputMode.AudioSource)
				{
					AudioSource targetAudioSource = videoPlayer.GetTargetAudioSource(num);
					if (targetAudioSource != null)
					{
						targetAudioSource.volume = info.weight;
					}
				}
			}
		}

		public override void OnGraphStart(Playable playable)
		{
			playedOnce = false;
		}

		public override void OnGraphStop(Playable playable)
		{
			if (!Application.isPlaying)
			{
				StopVideo();
			}
		}

		public override void OnPlayableDestroy(Playable playable)
		{
			StopVideo();
		}

		public void PlayVideo()
		{
			if (!(videoPlayer == null))
			{
				videoPlayer.Play();
				preparing = false;
				if (!Application.isPlaying)
				{
					PauseVideo();
				}
			}
		}

		public void PauseVideo()
		{
			if (!(videoPlayer == null))
			{
				videoPlayer.Pause();
				preparing = false;
			}
		}

		public void StopVideo()
		{
			if (!(videoPlayer == null))
			{
				playedOnce = false;
				videoPlayer.Stop();
				preparing = false;
			}
		}

		private void SyncVideoToPlayable(Playable playable)
		{
			if (!(videoPlayer == null) && !(videoPlayer.clip == null))
			{
				videoPlayer.time = (clipInTime + playable.GetTime() * (double)videoPlayer.playbackSpeed) % videoPlayer.clip.length;
			}
		}
	}
}
