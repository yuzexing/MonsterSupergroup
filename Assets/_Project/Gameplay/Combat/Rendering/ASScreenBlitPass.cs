using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace AstralShift.Rendering
{
	public class ASScreenBlitPass : ScriptableRenderPass
	{
		private class PassData
		{
			public TextureHandle source;
		}

		private int _fullScreenBlitTexPropID = Shader.PropertyToID("_ScreenBlitTex");

		public ASScreenBlitPass()
		{
			base.requiresIntermediateTexture = true;
		}

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			TextureHandle texture = frameData.Get<UniversalResourceData>().activeColorTexture;
			TextureDesc desc = renderGraph.GetTextureDesc(in texture);
			desc.name = "Screen Blit Texture";
			desc.clearBuffer = false;
			TextureHandle tex = renderGraph.CreateTexture(in desc);
			PassData passData;
			using IRasterRenderGraphBuilder rasterRenderGraphBuilder = renderGraph.AddRasterRenderPass<PassData>("Astral Shift - Screen Blit Pass", out passData, "C:\\Users\\Hizagui-Tower\\Documents\\Repositories\\Divina\\Assets\\Scripts\\AstralShift\\Rendering\\RendererFeatures\\ASScreenBlitPass.cs", 33);
			passData.source = texture;
			rasterRenderGraphBuilder.UseTexture(in texture);
			rasterRenderGraphBuilder.SetRenderAttachment(tex, 0, AccessFlags.ReadWrite);
			rasterRenderGraphBuilder.SetRenderFunc(delegate(PassData data, RasterGraphContext ctx)
			{
				Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), 0f, bilinear: false);
			});
			rasterRenderGraphBuilder.SetGlobalTextureAfterPass(in tex, _fullScreenBlitTexPropID);
		}
	}
}
