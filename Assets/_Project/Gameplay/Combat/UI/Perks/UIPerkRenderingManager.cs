using System.Collections.Generic;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Data.Perks;
using AstralShift.Managers;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Perks
{
	[RequireComponent(typeof(Camera))]
	[DefaultExecutionOrder(100000)]
	public class UIPerkRenderingManager : MonoBehaviour
	{
		public static UIPerkRenderingManager Instance;

		[SerializeField]
		private RenderTexture renderTextureAsset;

		private Camera _camera;

		public Transform pivot;

		private Dictionary<PerkView, Perk3DView> _viewTo3DViewLut;

		private Dictionary<Perk3DView, PerkView> _3DViewToViewLut;

		private Dictionary<Perk3DView, RenderTexture> _dynamicTextures;

		private Dictionary<RuntimePerkData, Perk3DView> _dataTo3DViewLut;

		private Dictionary<RuntimePerkData, RenderTexture> _staticTextures;

		private RuntimePerkDataEqualityComparer _dataEqualityComparer;

		private List<Perk3DView> _list;

		public RenderTexture RenderTextureAsset => renderTextureAsset;

		public Camera Camera => _camera;

		private void Awake()
		{
			if (Instance == null)
			{
				Instance = this;
			}
			TryGetComponent<Camera>(out _camera);
			_viewTo3DViewLut = new Dictionary<PerkView, Perk3DView>();
			_3DViewToViewLut = new Dictionary<Perk3DView, PerkView>();
			_list = new List<Perk3DView>();
			_dynamicTextures = new Dictionary<Perk3DView, RenderTexture>();
			_staticTextures = new Dictionary<RuntimePerkData, RenderTexture>(new RuntimePerkDataEqualityComparer());
			_camera.enabled = false;
		}

		public void AddPerk(PerkView perkView, Perk3DView perk3DView)
		{
			if (!_viewTo3DViewLut.ContainsKey(perkView))
			{
				perk3DView.Initialize();
				perk3DView.name = perkView.PerkData.Data.Title + " (3D Perk View)";
				perk3DView.transform.SetParent(pivot);
				perk3DView.transform.localPosition = Vector3.zero;
				_viewTo3DViewLut.Add(perkView, perk3DView);
				_3DViewToViewLut.Add(perk3DView, perkView);
				_list.Add(perk3DView);
				CreateDynamicTexture(perkView, perk3DView);
				perkView.BindDynamicTexture();
				perk3DView.EnableVisibility(state: false);
			}
		}

		public void RemovePerk(PerkView perkView)
		{
			if (_viewTo3DViewLut.TryGetValue(perkView, out var value))
			{
				if ((bool)value)
				{
					_list.Remove(value);
					_3DViewToViewLut.Remove(value);
					DestroyDynamicTexture(value);
					Object.Destroy(value.gameObject);
				}
				_viewTo3DViewLut.Remove(perkView);
			}
		}

		private void CreateDynamicTexture(PerkView perkView, Perk3DView perk3DView)
		{
			RenderTexture renderTexture = new RenderTexture(RenderTextureAsset);
			renderTexture.name = "UI Perk Dynamic Texture - " + perkView.PerkData.Data.Title;
			renderTexture.Create();
			perk3DView.AssignTexture(renderTexture);
			_dynamicTextures.Add(perk3DView, renderTexture);
		}

		public RenderTexture GetDynamicTexture(PerkView perkView)
		{
			if (_viewTo3DViewLut.TryGetValue(perkView, out var value))
			{
				return _dynamicTextures.GetValueOrDefault(value, null);
			}
			return null;
		}

		public void DestroyDynamicTexture(Perk3DView perk3DView)
		{
			if (_dynamicTextures.Remove(perk3DView, out var value))
			{
				value.Release();
				value.DiscardContents(discardColor: true, discardDepth: true);
				Object.Destroy(value);
				value = null;
			}
		}

		public void TryCacheStaticTexture(RuntimePerkData data, Perk3DView perk3DView)
		{
			if (data != null)
			{
				RuntimePerkData runtimePerkData = data.Clone() as RuntimePerkData;
				if (runtimePerkData == null || !_staticTextures.ContainsKey(runtimePerkData))
				{
					RenderTexture renderTexture = new RenderTexture(RenderTextureAsset);
					renderTexture.name = "UI Perk Static Texture - " + data.Data.Title + " Lvl: " + data.Rarity;
					renderTexture.Create();
					_staticTextures.Add(runtimePerkData, renderTexture);
					perk3DView.Render(Camera, renderTexture);
				}
			}
		}

		private void DestroyStaticTexture(RuntimePerkData data)
		{
			if (_staticTextures.Remove(data, out var value))
			{
				value.Release();
				value.DiscardContents(discardColor: true, discardDepth: true);
				Object.Destroy(value);
				value = null;
			}
		}

		public RenderTexture GetStaticTexture(RuntimePerkData data)
		{
			return _staticTextures.GetValueOrDefault(data, null);
		}

		private void LateUpdate()
		{
			TryRenderDynamicTextures();
		}

		private void TryRenderDynamicTextures()
		{
			if (ControllerManager.Instance.CurrentController is PerkMenuController && _list.Count != 0)
			{
				for (int num = _list.Count - 1; num >= 0; num--)
				{
					_list[num].TryRender(Camera);
				}
			}
		}

		public Perk3DView GetPerk3DView(PerkView perkView)
		{
			if (!(perkView == null))
			{
				return _viewTo3DViewLut?.GetValueOrDefault(perkView);
			}
			return null;
		}
	}
}
