using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace AstralShift.Rendering
{
	[DisallowMultipleRendererFeature(null)]
	public class ASRendererFeature : ScriptableRendererFeature
	{
		[FormerlySerializedAs("settings")]
		[Space]
		[SerializeField]
		protected ASScreenBlurPass.Settings fullscreenBlurSettings;

		private ASPreRenderPass _preRenderPass;

		private ASPostProcessingPass _postProcessingPass;

		private ASScreenBlurPass _blurPass;

		private ASScreenBlitPass _screenBlitPass;

		private static ASRendererFeature _instance;

		private bool _isBlurEnabled;

		private float _blurDeactivationTimestamp;

		private const float BlurDeactivationTimeout = 1f;

		private bool _blurDeactivationRequested;

		public static ASRendererFeature Instance
		{
			get
			{
				if (_instance == null)
				{
					ASRendererFeature[] loadedFeatures = Resources.FindObjectsOfTypeAll<ASRendererFeature>();
					for (int i = 0; i < loadedFeatures.Length; i++)
					{
						ASRendererFeature candidate = loadedFeatures[i];
						if (candidate != null && candidate.isActive)
						{
							_instance = candidate;
							candidate.InitPassesIfNeeded();
							break;
						}
					}
				}
				return _instance;
			}
		}

		public override void Create()
		{
			// Unity 6 can create and cache renderer features before Play Mode starts.
			// Restricting this assignment to Application.isPlaying leaves the static
			// reference null after a domain reload even though the feature is active.
			_instance = this;
			InitPassesIfNeeded();
			base.name = "AstralShift Renderer Feature";
		}

		private void InitPassesIfNeeded()
		{
			if (_preRenderPass == null)
			{
				_preRenderPass = new ASPreRenderPass();
				_preRenderPass.renderPassEvent = RenderPassEvent.BeforeRendering;
			}
			if (_postProcessingPass == null)
			{
				_postProcessingPass = new ASPostProcessingPass();
				_postProcessingPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
			}
			if (_screenBlitPass == null)
			{
				_screenBlitPass = new ASScreenBlitPass();
				_screenBlitPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
			}
			if (_blurPass == null)
			{
				_blurPass = new ASScreenBlurPass();
				_blurPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
			}
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			_instance = this;
			if (!renderingData.cameraData.isSceneViewCamera && !renderingData.cameraData.isPreviewCamera)
			{
				InitPassesIfNeeded();
				renderer.EnqueuePass(_preRenderPass);
				renderer.EnqueuePass(_postProcessingPass);
				renderer.EnqueuePass(_screenBlitPass);
				_blurPass.Setup(in renderingData.cameraData.cameraTargetDescriptor, fullscreenBlurSettings);
				if (_isBlurEnabled || !Application.isPlaying)
				{
					renderer.EnqueuePass(_blurPass);
				}
				if (Application.isPlaying && _blurDeactivationRequested && Time.unscaledTime - _blurDeactivationTimestamp >= 1f)
				{
					_isBlurEnabled = false;
					_blurDeactivationRequested = false;
				}
			}
		}

		protected override void Dispose(bool disposing)
		{
			_preRenderPass = null;
			_postProcessingPass = null;
			_screenBlitPass = null;
			_blurPass?.Dispose();
			_blurPass = null;
			if (_instance == this)
			{
				_instance = null;
			}
			base.Dispose(disposing);
		}

		public void EnableFullscreenBlurRenderPass(bool enable)
		{
			if (enable)
			{
				_blurDeactivationRequested = false;
				_isBlurEnabled = true;
			}
			else
			{
				_blurDeactivationRequested = true;
				_blurDeactivationTimestamp = Time.unscaledTime;
			}
		}
	}
}
