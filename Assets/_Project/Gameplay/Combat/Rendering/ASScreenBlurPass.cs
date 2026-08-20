using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace AstralShift.Rendering
{
	public class ASScreenBlurPass : ScriptableRenderPass, IDisposable
	{
		[Serializable]
		public class Settings
		{
			[Range(1f, 8f)]
			public int downscaleFactor = 4;

			[Range(0f, 10f)]
			public float gaussianBlurStrength = 2f;
		}

		private class PassData
		{
			internal TextureHandle Source;

			internal TextureHandle Destination;

			internal TextureHandle[] MipUp;

			internal TextureHandle[] MipDown;

			internal int MipCount;

			internal Material BlurBlitMaterial;
		}

		private ProfilingSampler _profilingSampler;

		private static int _downscaleFactor;

		private static float _blurStrength;

		private TextureHandle[] _mipUp;

		private TextureHandle[] _mipDown;

		private RTHandle[] _mipDownRTHandles;

		private RTHandle[] _mipUpRTHandles;

		private int[] _mipUpPropIDs;

		private int[] _mipDownPropIDs;

		private RenderTextureDescriptor _cameraRTDescriptor;

		private static Material _blurBlitMaterial;

		private const string BlurBlitShaderPath = "Hidden/AstralShift/FullscreenBlurBlit";

		private static readonly int BlurBlitStrengthPropID = Shader.PropertyToID("_BlurStrength");

		private static readonly int TargetTexturePropID = Shader.PropertyToID("_FullscreenBlurTexture");

		private const int MaxPyramidSize = 16;

		private static ProfilingSampler _setupMipsProfilingSampler = new ProfilingSampler("ScreenBlur - Setup Mips");

		private static ProfilingSampler _blitMipsProfilingSampler = new ProfilingSampler("ScreenBlur - Blit Mipmaps");

		private static ProfilingSampler _upSampleProfilingSampler = new ProfilingSampler("ScreenBlur - UpSample");

		private static ProfilingSampler _downSampleProfilingSampler = new ProfilingSampler("ScreenBlur - DownSample");

		public ASScreenBlurPass()
		{
			base.requiresIntermediateTexture = false;
		}

		private void ReInitIfNeeded()
		{
			if (!_blurBlitMaterial)
			{
				_blurBlitMaterial = CreateMaterial("Hidden/AstralShift/FullscreenBlurBlit");
			}
			if (_mipDownRTHandles == null || _mipUpRTHandles == null)
			{
				_mipDownRTHandles = new RTHandle[16];
				_mipUpRTHandles = new RTHandle[16];
				_mipUp = new TextureHandle[16];
				_mipDown = new TextureHandle[16];
				_mipUpPropIDs = new int[16];
				_mipDownPropIDs = new int[16];
				for (int i = 0; i < 16; i++)
				{
					_mipUpPropIDs[i] = Shader.PropertyToID("_FullscreenBlurMipUp" + i);
					_mipDownPropIDs[i] = Shader.PropertyToID("_FullscreenBlurMipDown" + i);
					_mipUpRTHandles[i] = RTHandles.Alloc(_mipUpPropIDs[i], "_FullscreenBlurMipUp" + i);
					_mipDownRTHandles[i] = RTHandles.Alloc(_mipDownPropIDs[i], "_FullscreenBlurMipDown" + i);
				}
			}
		}

		public void Dispose()
		{
			if (_mipDownRTHandles != null)
			{
				RTHandle[] mipDownRTHandles = _mipDownRTHandles;
				foreach (RTHandle rTHandle in mipDownRTHandles)
				{
					if (rTHandle != null)
					{
						RTHandles.Release(rTHandle);
					}
				}
			}
			_mipDownRTHandles = null;
			if (_mipUpRTHandles != null)
			{
				RTHandle[] mipDownRTHandles = _mipUpRTHandles;
				foreach (RTHandle rTHandle2 in mipDownRTHandles)
				{
					if (rTHandle2 != null)
					{
						RTHandles.Release(rTHandle2);
					}
				}
			}
			_mipUpRTHandles = null;
			CoreUtils.Destroy(_blurBlitMaterial);
		}

		private Material CreateMaterial(string shaderPath)
		{
			Shader shader = Shader.Find(shaderPath);
			if ((bool)shader)
			{
				return CoreUtils.CreateEngineMaterial(shader);
			}
			Debug.LogErrorFormat("ScreenBlurPass: Could not find shader " + shaderPath + ".");
			return null;
		}

		public void Setup(in RenderTextureDescriptor descriptor, Settings settings)
		{
			_cameraRTDescriptor = descriptor;
			_downscaleFactor = settings.downscaleFactor;
			_blurStrength = settings.gaussianBlurStrength;
			ReInitIfNeeded();
		}

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			UniversalResourceData universalResourceData = frameData.Get<UniversalResourceData>();
			if (universalResourceData.isActiveTargetBackBuffer)
			{
				Debug.LogError("Skipping render pass. ScreenBlurPass requires an intermediate ColorTexture, we can't use the BackBuffer as a texture input.");
				return;
			}
			TextureHandle texture = universalResourceData.activeColorTexture;
			TextureDesc desc = renderGraph.GetTextureDesc(in texture);
			desc.name = "Screen Blur Texture";
			desc.clearBuffer = false;
			TextureHandle destination = renderGraph.CreateTexture(in desc);
			int num = Mathf.Max(1, _cameraRTDescriptor.width >> _downscaleFactor);
			int num2 = Mathf.Max(1, _cameraRTDescriptor.height >> _downscaleFactor);
			int num3 = Mathf.FloorToInt(Mathf.Log(Mathf.Max(num, num2), 2f) - 1f);
			using (new ProfilingScope(_setupMipsProfilingSampler))
			{
				RenderTextureDescriptor desc2 = new RenderTextureDescriptor(num, num2, _cameraRTDescriptor.graphicsFormat, 0);
				_mipDown[0] = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc2, _mipDownRTHandles[0].name, clear: false, FilterMode.Bilinear);
				_mipUp[0] = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc2, _mipUpRTHandles[0].name, clear: false, FilterMode.Bilinear);
				for (int i = 1; i < num3; i++)
				{
					num = Mathf.Max(1, num >> 1);
					num2 = Mathf.Max(1, num2 >> 1);
					ref TextureHandle reference = ref _mipDown[i];
					ref TextureHandle reference2 = ref _mipUp[i];
					desc2.width = num;
					desc2.height = num2;
					reference = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc2, _mipDownRTHandles[i].name, clear: true, FilterMode.Bilinear);
					reference2 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc2, _mipUpRTHandles[i].name, clear: true, FilterMode.Bilinear);
				}
			}
			PassData passData;
			using IUnsafeRenderGraphBuilder unsafeRenderGraphBuilder = renderGraph.AddUnsafePass<PassData>("Screen Blur Pass", out passData, _blitMipsProfilingSampler, "C:\\Users\\Hizagui-Tower\\Documents\\Repositories\\Divina\\Assets\\Scripts\\AstralShift\\Rendering\\RendererFeatures\\Screen Blur Pass\\ASScreenBlurPass.cs", 215);
			passData.Source = texture;
			passData.Destination = destination;
			passData.BlurBlitMaterial = _blurBlitMaterial;
			passData.MipCount = num3;
			passData.MipDown = _mipDown;
			passData.MipUp = _mipUp;
			passData.BlurBlitMaterial.SetFloat(BlurBlitStrengthPropID, _blurStrength);
			unsafeRenderGraphBuilder.AllowPassCulling(value: false);
			unsafeRenderGraphBuilder.UseTexture(in texture);
			unsafeRenderGraphBuilder.UseTexture(in destination, AccessFlags.ReadWrite);
			for (int j = 0; j < num3; j++)
			{
				unsafeRenderGraphBuilder.UseTexture(in _mipDown[j], AccessFlags.ReadWrite);
				unsafeRenderGraphBuilder.UseTexture(in _mipUp[j], AccessFlags.ReadWrite);
			}
			unsafeRenderGraphBuilder.SetRenderFunc(delegate(PassData data, UnsafeGraphContext context)
			{
				CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
				int mipCount = data.MipCount;
				RenderBufferLoadAction loadAction = RenderBufferLoadAction.DontCare;
				RenderBufferStoreAction storeAction = RenderBufferStoreAction.Store;
				Material blitMaterial = Blitter.GetBlitMaterial(TextureDimension.Tex2D, singleSlice: true);
				Blitter.BlitCameraTexture(nativeCommandBuffer, data.Source, data.MipDown[0], loadAction, storeAction, blitMaterial, 0);
				using (new ProfilingScope(nativeCommandBuffer, _downSampleProfilingSampler))
				{
					TextureHandle textureHandle = data.MipDown[0];
					for (int k = 1; k < mipCount; k++)
					{
						TextureHandle textureHandle2 = data.MipDown[k];
						Blitter.BlitCameraTexture(nativeCommandBuffer, textureHandle, textureHandle2, loadAction, storeAction, data.BlurBlitMaterial, 1);
						textureHandle = textureHandle2;
					}
				}
				using (new ProfilingScope(nativeCommandBuffer, _upSampleProfilingSampler))
				{
					for (int num4 = mipCount - 1; num4 > 0; num4--)
					{
						TextureHandle textureHandle3 = data.MipDown[num4];
						TextureHandle textureHandle4 = data.MipUp[num4 - 1];
						Blitter.BlitCameraTexture(nativeCommandBuffer, textureHandle3, textureHandle4, loadAction, storeAction, data.BlurBlitMaterial, 2);
					}
					data.Destination = data.MipUp[0];
					nativeCommandBuffer.SetGlobalTexture(TargetTexturePropID, data.Destination);
				}
			});
		}
	}
}
