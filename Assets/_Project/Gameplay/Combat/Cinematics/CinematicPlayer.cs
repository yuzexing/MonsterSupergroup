using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UI;
using UnityEngine.Video;

namespace AstralShift.Cinematics
{
	[RequireComponent(typeof(VideoPlayer))]
	public class CinematicPlayer : MonoBehaviour
	{
		[Serializable]
		internal enum PlayBackMode
		{
			VideoPlayer = 0,
			FMOD = 1
		}

		[Serializable]
		internal enum RenderTextureMode
		{
			Manual = 0,
			Auto = 1
		}

		[SerializeField]
		private PlayBackMode playbackMode;

		public VideoPlayer VideoPlayer;

		public FMODVideoPlaybackSync FMODSync;

		public VideoPlaybackSync VideoSync;

		protected VideoPlaybackSync _activeSyncScript;

		public bool OnAwakePrewarm;

		public bool PlayOnAwake;

		public bool isSkippable = true;

		[Tooltip("Time point to skip to instead of the end of the video")]
		public double skipPoint = -1.0;

		public GameObject skipObject;

		private bool _isShowingSkipObject;

		public float skipHoldTime = 3f;

		private float _elapsedSkipTime;

		[SerializeField]
		private RenderTextureMode renderTextureMode;

		[SerializeField]
		protected RawImage rawImage;

		private RenderTexture _renderTexture;

		public RawImage RawImage => rawImage;

		public bool IsPreWarmed
		{
			get
			{
				if ((bool)_activeSyncScript)
				{
					return _activeSyncScript.videoPlayer.isPrepared;
				}
				return false;
			}
		}

		public event Action OnVideoStop;

		public event Action OnVideoPause;

		public event Action OnVideoSkip;

		public event Action OnBeforePlay;

		private void GetSyncScript()
		{
			if (playbackMode == PlayBackMode.VideoPlayer)
			{
				VideoSync = base.gameObject.GetComponent<VideoPlaybackSync>();
				_activeSyncScript = VideoSync;
			}
			else
			{
				FMODSync = base.gameObject.GetComponent<FMODVideoPlaybackSync>();
				_activeSyncScript = FMODSync;
			}
			_activeSyncScript.videoPlayer = VideoPlayer;
		}

		private void Awake()
		{
			GetSyncScript();
			if (OnAwakePrewarm)
			{
				PreWarm();
			}
			if (PlayOnAwake)
			{
				StartVideo();
			}
		}

		private void OnDestroy()
		{
			DestroyRenderTexture();
		}

		private void Update()
		{
			if (_isShowingSkipObject && (bool)skipObject)
			{
				_elapsedSkipTime += Time.unscaledDeltaTime;
				if (_elapsedSkipTime >= skipHoldTime)
				{
					skipObject.SetActive(value: false);
				}
			}
		}

		public void StartVideo()
		{
			this.OnBeforePlay?.Invoke();
			if (renderTextureMode == RenderTextureMode.Auto && (bool)rawImage)
			{
				if ((bool)_renderTexture && _renderTexture.height != (int)VideoPlayer.clip.height)
				{
					DestroyRenderTexture();
				}
				if (!_renderTexture)
				{
					CreateNewRenderTexture();
				}
				VideoPlayer.targetTexture = _renderTexture;
				rawImage.texture = _renderTexture;
			}
			_activeSyncScript.Play();
		}

		public void PreWarm()
		{
			GetSyncScript();
			_activeSyncScript.Init();
		}

		private void CreateNewRenderTexture()
		{
			if ((bool)VideoPlayer.clip)
			{
				int width = (int)VideoPlayer.clip.width;
				int height = (int)VideoPlayer.clip.height;
				RenderTextureDescriptor desc = new RenderTextureDescriptor(width, height, GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormat.D16_UNorm);
				_renderTexture = new RenderTexture(desc);
				_renderTexture.filterMode = FilterMode.Point;
			}
		}

		private void DestroyRenderTexture()
		{
			if ((bool)_renderTexture)
			{
				_renderTexture.Release();
				UnityEngine.Object.Destroy(_renderTexture);
				_renderTexture = null;
			}
		}

		public void SetOnVideoEndCallback(Action callback)
		{
			_activeSyncScript.OnVideoEnd = callback;
		}

		public void PauseVideo()
		{
			_activeSyncScript.SetPaused(paused: true);
		}

		public void ResumeVideo()
		{
			_activeSyncScript.SetPaused(paused: false);
		}

		public void SkipVideo()
		{
			if (isSkippable)
			{
				_activeSyncScript.Skip(skipPoint);
				this.OnVideoSkip?.Invoke();
			}
		}

		public void StopVideo()
		{
			VideoPlayer.Stop();
			this.OnVideoStop?.Invoke();
		}

		public void ShowSkipText()
		{
			_isShowingSkipObject = true;
			_elapsedSkipTime = 0f;
			skipObject.SetActive(value: true);
		}

		private void OnControllerDisconnect()
		{
			if ((bool)_activeSyncScript)
			{
				_activeSyncScript.SetPaused(paused: true);
			}
		}

		private void OnControllerConnected()
		{
			if ((bool)_activeSyncScript)
			{
				_activeSyncScript.SetPaused(paused: false);
			}
		}
	}
}
