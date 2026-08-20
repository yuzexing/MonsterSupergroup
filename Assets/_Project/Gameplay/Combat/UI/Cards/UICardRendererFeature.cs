using System.Collections.Generic;
using AstralShift.HellMaiden.Controllers;
using AstralShift.Managers;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace AstralShift.HellMaiden.UI.Cards
{
	public class UICardRendererFeature : ScriptableRendererFeature
	{
		private class UICardRendererPass : ScriptableRenderPass
		{
			private class PassData
			{
				public RendererListHandle RendererListHandle;

				public TextureHandle TextureHandle;

				public Matrix4x4 ViewMatrix;

				public Matrix4x4 ProjMatrix;
			}

			private LayerMask _layerMask;

			private List<ShaderTagId> _shaderTagIDList = new List<ShaderTagId>();

			private const string _passName = "UI Cards Renderer Pass";

			private RTHandle _rtHandle;

			public UICardRendererPass(LayerMask layerMask)
			{
				_layerMask = layerMask;
			}

			private void InitRendererLists(ContextContainer frameData, ref PassData passData, RenderGraph renderGraph)
			{
				frameData.Get<UniversalRenderingData>();
				UniversalCameraData universalCameraData = frameData.Get<UniversalCameraData>();
				frameData.Get<UniversalLightData>();
				_ = universalCameraData.defaultOpaqueSortFlags;
				FilteringSettings filteringSettings = new FilteringSettings
				{
					renderQueueRange = RenderQueueRange.all,
					layerMask = _layerMask,
					renderingLayerMask = uint.MaxValue,
					sortingLayerRange = SortingLayerRange.all
				};
				_ = new ShaderTagId[6]
				{
					new ShaderTagId("Universal2D"),
					new ShaderTagId("NormalsRendering"),
					new ShaderTagId("UniversalForwardOnly"),
					new ShaderTagId("UniversalForward"),
					new ShaderTagId("SRPDefaultUnlit"),
					new ShaderTagId("LightweightForward")
				};
				_shaderTagIDList.Clear();
			}

			private static void ExecutePass(PassData data, RasterGraphContext context)
			{
				context.cmd.ClearRenderTarget(RTClearFlags.All, Color.clear, 1f, 0u);
				context.cmd.SetViewProjectionMatrices(data.ViewMatrix, data.ProjMatrix);
				context.cmd.DrawRendererList(data.RendererListHandle);
				RTHandles.Release(data.TextureHandle);
			}

			public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
			{
				if (frameData.Get<UniversalCameraData>().cameraType == CameraType.Game && (!(ControllerManager.Instance != null) || ControllerManager.Instance.CurrentController is CardPickMenuController))
				{
					_ = UICardRenderingManager.Instance == null;
				}
			}
		}

		public static UICardRendererFeature Instance;

		[SerializeField]
		protected LayerMask layerMask;

		private UICardRendererPass _pass;

		private Dictionary<UICard3DView, RenderTexture> _lut;

		public override void Create()
		{
			if (Instance == null)
			{
				Instance = this;
			}
			_pass = new UICardRendererPass(layerMask)
			{
				renderPassEvent = RenderPassEvent.AfterRenderingTransparents
			};
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			renderer.EnqueuePass(_pass);
		}
	}
}
