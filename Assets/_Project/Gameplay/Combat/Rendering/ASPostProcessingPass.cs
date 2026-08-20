using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace AstralShift.Rendering
{
	public class ASPostProcessingPass : ScriptableRenderPass
	{
		public enum ColorBlindModeEnum
		{
			None = 0,
			Protanomaly = 1,
			Deuteranomaly = 2,
			Tritanomaly = 3
		}

		internal class PassData
		{
			internal TextureHandle Source;

			internal TextureHandle Destination;

			internal Material BlitMaterial;
		}

		private ProfilingSampler _profilingSampler;

		private static string _passName;

		private static Material _blitMaterial;

		private const string ShaderName = "Hidden/AstralShift/PostProcessingPass";

		private const int ShaderColorCorrectionPassIndex = 0;

		private NegativeColorVolume _negativeColorVolume;

		private const string NegativeColorKeyword = "NEGATIVECOLOR_ON";

		private const string NegativeColorRoundMaskKeyword = "NEGATIVECOLOR_ROUNDMASK";

		private readonly int NegativeColorRoundMaskProgressPropID = Shader.PropertyToID("_NegativeColorRoundMaskProgress");

		public static void SetColorBlindMode(ColorBlindModeEnum mode, float strength)
		{
			Shader.SetGlobalFloat("_ASColorBlindStrength", strength);
			switch (mode)
			{
			case ColorBlindModeEnum.None:
				Shader.EnableKeyword("COLORBLIND_NONE");
				Shader.DisableKeyword("COLORBLIND_PROTANOPIA");
				Shader.DisableKeyword("COLORBLIND_DEUTERANOPIA");
				Shader.DisableKeyword("COLORBLIND_TRITANOPIA");
				break;
			case ColorBlindModeEnum.Protanomaly:
				Shader.DisableKeyword("COLORBLIND_NONE");
				Shader.EnableKeyword("COLORBLIND_PROTANOPIA");
				Shader.DisableKeyword("COLORBLIND_DEUTERANOPIA");
				Shader.DisableKeyword("COLORBLIND_TRITANOPIA");
				break;
			case ColorBlindModeEnum.Deuteranomaly:
				Shader.DisableKeyword("COLORBLIND_NONE");
				Shader.DisableKeyword("COLORBLIND_PROTANOPIA");
				Shader.EnableKeyword("COLORBLIND_DEUTERANOPIA");
				Shader.DisableKeyword("COLORBLIND_TRITANOPIA");
				break;
			case ColorBlindModeEnum.Tritanomaly:
				Shader.DisableKeyword("COLORBLIND_NONE");
				Shader.DisableKeyword("COLORBLIND_PROTANOPIA");
				Shader.DisableKeyword("COLORBLIND_DEUTERANOPIA");
				Shader.EnableKeyword("COLORBLIND_TRITANOPIA");
				break;
			}
		}

		public static void SetBrightness(float value)
		{
			value = Mathf.Clamp(value, 0.1f, 2f);
			Shader.SetGlobalFloat("_ASGlobalBrightness", value);
		}

		public static void SetGamma(float value)
		{
			value = Mathf.Clamp(value, 0.1f, 2f);
			Shader.SetGlobalFloat("_ASGlobalGamma", value);
		}

		public static void SetContrast(float value)
		{
			value = Mathf.Clamp(value, 0.1f, 2f);
			Shader.SetGlobalFloat("_ASGlobalContrast", value);
		}

		public ASPostProcessingPass()
		{
			_passName = "Astral Shift - Post Processing Pass";
			CreateBlitMaterial();
			base.requiresIntermediateTexture = true;
			if (!Application.isPlaying)
			{
				SetBrightness(1f);
				SetGamma(1f);
				SetContrast(1f);
			}
		}

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			GetVolumeComponentsOverrides(frameData);
			PassData passData;
			using IUnsafeRenderGraphBuilder unsafeRenderGraphBuilder = renderGraph.AddUnsafePass<PassData>("Astral Shift - Post Processing Pass", out passData, _profilingSampler, "C:\\Users\\Hizagui-Tower\\Documents\\Repositories\\Divina\\Assets\\Scripts\\AstralShift\\Rendering\\RendererFeatures\\Post Processing Pass\\ASPostProcessingPass.cs", 110);
			ConfigureInput(ScriptableRenderPassInput.Color);
			UniversalResourceData universalResourceData = frameData.Get<UniversalResourceData>();
			if (universalResourceData.isActiveTargetBackBuffer)
			{
				Debug.LogError("Skipping render pass. AfterRendering requires an intermediate ColorTexture, we can't use the BackBuffer as a texture input.");
				return;
			}
			TextureHandle texture = universalResourceData.activeColorTexture;
			TextureDesc desc = renderGraph.GetTextureDesc(in texture);
			desc.name = "CameraColor-" + _passName;
			desc.clearBuffer = false;
			TextureHandle destination = renderGraph.CreateTexture(in desc);
			if (!_blitMaterial)
			{
				_blitMaterial = new Material(Shader.Find("Hidden/AstralShift/PostProcessingPass"));
			}
			passData.Source = texture;
			passData.Destination = destination;
			passData.BlitMaterial = _blitMaterial;
			unsafeRenderGraphBuilder.UseTexture(in texture, AccessFlags.ReadWrite);
			unsafeRenderGraphBuilder.UseTexture(in destination, AccessFlags.ReadWrite);
			unsafeRenderGraphBuilder.AllowPassCulling(value: false);
			unsafeRenderGraphBuilder.AllowGlobalStateModification(value: true);
			unsafeRenderGraphBuilder.SetRenderFunc(delegate(PassData data, UnsafeGraphContext context)
			{
				CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
				Blitter.BlitCameraTexture(nativeCommandBuffer, data.Source, data.Destination, data.BlitMaterial, 0);
				nativeCommandBuffer.CopyTexture(data.Destination, data.Source);
			});
		}

		private void CreateBlitMaterial()
		{
			_blitMaterial = new Material(Shader.Find("Hidden/AstralShift/PostProcessingPass"));
		}

		private void GetVolumeComponentsOverrides(ContextContainer frameData)
		{
			if (!frameData.Get<UniversalCameraData>().postProcessEnabled)
			{
				SetKeyword("NEGATIVECOLOR_ON", state: false);
			}
			else
			{
				ProcessNegativeColorOverrides();
			}
		}

		private void ProcessNegativeColorOverrides()
		{
			_negativeColorVolume = VolumeManager.instance.stack.GetComponent<NegativeColorVolume>();
			bool flag = _negativeColorVolume != null && _negativeColorVolume.IsActive();
			if (!_blitMaterial)
			{
				CreateBlitMaterial();
			}
			if (_blitMaterial.IsKeywordEnabled("NEGATIVECOLOR_ON") != flag)
			{
				SetKeyword("NEGATIVECOLOR_ON", flag);
			}
			if (flag)
			{
				bool flag2 = _negativeColorVolume.IsRoundMaskEnabled();
				if (_blitMaterial.IsKeywordEnabled("NEGATIVECOLOR_ROUNDMASK") != flag2)
				{
					SetKeyword("NEGATIVECOLOR_ROUNDMASK", flag2);
				}
				_blitMaterial.SetFloat(NegativeColorRoundMaskProgressPropID, _negativeColorVolume.GetRoundMaskProgress());
			}
		}

		private void SetKeyword(string keyword, bool state)
		{
			if (state)
			{
				_blitMaterial.EnableKeyword(keyword);
			}
			else
			{
				_blitMaterial.DisableKeyword(keyword);
			}
		}
	}
}
