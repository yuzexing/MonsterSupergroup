using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace AstralShift.Rendering
{
	public class ASPreRenderPass : ScriptableRenderPass
	{
		private class UnSafePassData
		{
		}

		private ProfilingSampler _profilingSampler;

		private static string _passName;

		public static event Action BeforeRenderCallback;

		public ASPreRenderPass()
		{
			_passName = "AstralShift - Pre Render Pass";
		}

		private static void ExecuteUnSafePass(UnSafePassData data, UnsafeGraphContext context)
		{
			ASPreRenderPass.BeforeRenderCallback?.Invoke();
		}

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			UnSafePassData passData;
			using IUnsafeRenderGraphBuilder unsafeRenderGraphBuilder = renderGraph.AddUnsafePass<UnSafePassData>(_passName, out passData, "C:\\Users\\Hizagui-Tower\\Documents\\Repositories\\Divina\\Assets\\Scripts\\AstralShift\\Rendering\\RendererFeatures\\ASPreRenderPass.cs", 36);
			unsafeRenderGraphBuilder.AllowPassCulling(value: false);
			unsafeRenderGraphBuilder.SetRenderFunc(delegate(UnSafePassData data, UnsafeGraphContext context)
			{
				ExecuteUnSafePass(data, context);
			});
		}
	}
}
