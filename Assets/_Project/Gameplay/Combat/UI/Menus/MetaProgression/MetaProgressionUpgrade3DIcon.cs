using AstralShift.Helpers;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Menus.MetaProgression
{
	public class MetaProgressionUpgrade3DIcon : UIGeneric3DRenderer
	{
		[SerializeField]
		private MeshRenderer iconRenderer;

		[SerializeField]
		private MeshRenderer mainGemRenderer;

		[SerializeField]
		private MeshRenderer smallGemRenderer;

		[SerializeField]
		private MeshRenderer[] renderers;

		private readonly int IconTexturePropID = Shader.PropertyToID("_BaseMap");

		private readonly int ViewPortPositionShaderPropID = Shader.PropertyToID("_ViewPortPosition");

		public void SetIcon(Sprite icon)
		{
			SpriteHelpers.SetTextureWithAtlasSupport(icon, iconRenderer.material, IconTexturePropID);
		}

		public void SetGemMaterials(Material main, Material secondary)
		{
			mainGemRenderer.sharedMaterial = main;
			smallGemRenderer.sharedMaterial = secondary;
		}

		public void SetViewPortPosition(Vector2 position, float depthOffset = 10f)
		{
			_currentPosition = position;
			_transform.localPosition = new Vector3(0f, 0f, depthOffset);
			ApplyShaderPropertyToMeshRenderers(ViewPortPositionShaderPropID, position);
		}

		public void Rotate(Vector3 eulerAngles)
		{
			base.transform.eulerAngles = eulerAngles;
		}

		private void ApplyShaderPropertyToMeshRenderers(int propID, Vector4 value)
		{
			for (int i = 0; i < renderers.Length; i++)
			{
				Material[] materials = renderers[i].materials;
				foreach (Material material in materials)
				{
					if (material.HasProperty(propID))
					{
						material.SetVector(propID, value);
					}
				}
			}
		}
	}
}
