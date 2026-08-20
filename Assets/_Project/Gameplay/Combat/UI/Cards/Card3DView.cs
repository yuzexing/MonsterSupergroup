using System.Collections.Generic;
using AstralShift.Helpers;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Cards
{
	public class Card3DView : MonoBehaviour, IEquipmentCardVisual, ICardVisual
	{
		[Header("Illustration")]
		[SerializeField]
		protected MeshRenderer illustrationMain;

		[SerializeField]
		protected Transform illustrationAdditionalParent;

		[Header("Frame")]
		[SerializeField]
		protected MeshRenderer frameBackground;

		[SerializeField]
		protected MeshRenderer frameMain;

		[Header("Foreground")]
		[SerializeField]
		protected Transform foregroundContainer;

		[Header("Text Box")]
		[SerializeField]
		protected MeshRenderer textBoxBackground;

		[SerializeField]
		protected TextMeshPro titleText;

		[SerializeField]
		protected Transform fullDescriptionParent;

		[SerializeField]
		protected TextMeshPro fullDescriptionText;

		[SerializeField]
		protected Transform shortDescriptionParent;

		[SerializeField]
		protected TextMeshPro shortDescriptionText;

		[SerializeField]
		protected Image quoteSeparator;

		[SerializeField]
		protected TextMeshPro quoteText;

		[SerializeField]
		protected MeshRenderer effect;

		[SerializeField]
		protected MeshRenderer level;

		private int _mainTexPropID = Shader.PropertyToID("_MainTex");

		[SerializeField]
		[HideInInspector]
		protected List<Renderer> _renderers;

		public List<Renderer> Renderers => _renderers;

		private void SetMaterialSafe(MeshRenderer renderer, Material sourceMat)
		{
			if ((bool)renderer && (bool)sourceMat && Application.isPlaying)
			{
				renderer.material = sourceMat;
			}
		}

		private Material GetActiveMaterial(MeshRenderer renderer)
		{
			if (!Application.isPlaying)
			{
				return renderer.sharedMaterial;
			}
			return renderer.material;
		}

		public virtual void SetIllustrationMainLayer(Sprite sprite, Material material = null)
		{
			if ((bool)illustrationMain && (bool)sprite)
			{
				SetMaterialSafe(illustrationMain, (material != null) ? material : illustrationMain.sharedMaterial);
				SpriteHelpers.SetTextureWithAtlasSupport(sprite, GetActiveMaterial(illustrationMain), _mainTexPropID);
				TryAddToRenderersList(illustrationMain);
			}
		}

		public virtual async UniTask SetIllustrationAdditionalLayer(int index, Sprite sprite, Material material = null)
		{
			if ((bool)sprite)
			{
				MeshRenderer meshRenderer;
				if (Application.isPlaying)
				{
					AsyncInstantiateOperation<MeshRenderer> op = Object.InstantiateAsync(illustrationMain, illustrationAdditionalParent);
					await op;
					meshRenderer = op.Result[0];
				}
				else
				{
					meshRenderer = Object.Instantiate(illustrationMain, illustrationAdditionalParent);
				}
				meshRenderer.transform.SetSiblingIndex(index);
				meshRenderer.name = "IllustrationAdditional_Layer_" + illustrationAdditionalParent.childCount;
				Material sourceMat = (material ? material : illustrationMain.sharedMaterial);
				SetMaterialSafe(meshRenderer, sourceMat);
				Material activeMaterial = GetActiveMaterial(meshRenderer);
				activeMaterial.renderQueue = GetActiveMaterial(illustrationMain).renderQueue + index + 1;
				SpriteHelpers.SetTextureWithAtlasSupport(sprite, activeMaterial, _mainTexPropID);
				TryAddToRenderersList(meshRenderer);
			}
		}

		public virtual void ClearIllustrationAdditionalLayers()
		{
			if (illustrationAdditionalParent.childCount == 0)
			{
				return;
			}
			List<Transform> list = new List<Transform>();
			for (int i = 0; i < illustrationAdditionalParent.childCount; i++)
			{
				list.Add(illustrationAdditionalParent.GetChild(i));
			}
			for (int num = list.Count - 1; num >= 0; num--)
			{
				if (list[num].TryGetComponent<MeshRenderer>(out var component))
				{
					RemoveFromRenderersList(component);
					if (!Application.isPlaying)
					{
						_ = component.sharedMaterial != null;
					}
				}
				Object.DestroyImmediate(list[num].gameObject);
			}
			list.Clear();
		}

		public virtual void SetFrameLayer(Sprite bg, Sprite frame, Material bgMaterial = null, Material frameMaterial = null)
		{
			if ((bool)frameBackground)
			{
				if ((bool)bg)
				{
					frameBackground.enabled = true;
					SetMaterialSafe(frameBackground, (bgMaterial != null) ? bgMaterial : frameBackground.sharedMaterial);
					SpriteHelpers.SetTextureWithAtlasSupport(bg, GetActiveMaterial(frameBackground), _mainTexPropID);
				}
				else
				{
					frameBackground.enabled = false;
				}
				TryAddToRenderersList(frameBackground);
			}
			if ((bool)frameMain)
			{
				if ((bool)frame)
				{
					frameMain.enabled = true;
					SetMaterialSafe(frameMain, (frameMaterial != null) ? frameMaterial : frameMain.sharedMaterial);
					SpriteHelpers.SetTextureWithAtlasSupport(frame, GetActiveMaterial(frameMain), _mainTexPropID);
				}
				else
				{
					frameMain.enabled = false;
				}
				TryAddToRenderersList(frameMain);
			}
		}

		public virtual async UniTask SetForegroundLayer(Sprite sprite, Material material = null)
		{
			if ((bool)sprite)
			{
				MeshRenderer meshRenderer;
				if (Application.isPlaying)
				{
					AsyncInstantiateOperation<MeshRenderer> op = Object.InstantiateAsync(illustrationMain, foregroundContainer.transform);
					await op;
					meshRenderer = op.Result[0];
				}
				else
				{
					meshRenderer = Object.Instantiate(illustrationMain, foregroundContainer.transform);
				}
				meshRenderer.name = "Foreground_Layer_" + foregroundContainer.childCount;
				SetMaterialSafe(meshRenderer, (material != null) ? material : illustrationMain.sharedMaterial);
				SpriteHelpers.SetTextureWithAtlasSupport(sprite, GetActiveMaterial(meshRenderer), _mainTexPropID);
				TryAddToRenderersList(meshRenderer);
			}
		}

		public virtual void SetTitleText(string text, Color color)
		{
			if ((bool)titleText)
			{
				titleText.enabled = true;
				titleText.SetText(text);
				titleText.color = color;
				titleText.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
			}
		}

		public virtual void SetTextBoxBackground(Sprite sprite)
		{
			if ((bool)textBoxBackground)
			{
				textBoxBackground.enabled = true;
				if ((bool)sprite)
				{
					SetMaterialSafe(textBoxBackground, textBoxBackground.sharedMaterial);
					SpriteHelpers.SetTextureWithAtlasSupport(sprite, GetActiveMaterial(textBoxBackground), _mainTexPropID);
				}
				TryAddToRenderersList(textBoxBackground);
			}
			else if ((bool)textBoxBackground)
			{
				textBoxBackground.enabled = false;
			}
		}

		public virtual void SetDescriptionText(string text, Color color)
		{
			if ((bool)fullDescriptionText && (bool)fullDescriptionParent && (bool)shortDescriptionParent)
			{
				fullDescriptionParent.gameObject.SetActive(value: true);
				fullDescriptionText.enabled = true;
				fullDescriptionText.SetText(text);
				fullDescriptionText.color = color;
				fullDescriptionText.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
				shortDescriptionParent.gameObject.SetActive(value: false);
			}
		}

		public virtual void SetQuoteText(string text, Color color, Color separatorColor)
		{
			if ((bool)quoteText && !string.IsNullOrEmpty(text) && (bool)shortDescriptionParent && (bool)fullDescriptionParent)
			{
				fullDescriptionParent.gameObject.SetActive(value: false);
				shortDescriptionParent.gameObject.SetActive(value: true);
				if ((bool)quoteSeparator)
				{
					quoteSeparator.color = separatorColor;
				}
				quoteText.SetText(text);
				quoteText.color = color;
				quoteText.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
			}
		}

		public void SetLevelIcon(Sprite sprite)
		{
			if ((bool)level)
			{
				SetMaterialSafe(level, level.sharedMaterial);
				SpriteHelpers.SetTextureWithAtlasSupport(sprite, GetActiveMaterial(level), _mainTexPropID);
				TryAddToRenderersList(level);
			}
		}

		public void SetEffectIcon(Sprite sprite)
		{
			if ((bool)effect)
			{
				SetMaterialSafe(effect, effect.sharedMaterial);
				SpriteHelpers.SetTextureWithAtlasSupport(sprite, GetActiveMaterial(effect), _mainTexPropID);
				TryAddToRenderersList(effect);
			}
		}

		private void TryAddToRenderersList(MeshRenderer renderer)
		{
			if ((bool)renderer && !_renderers.Contains(renderer))
			{
				_renderers.Add(renderer);
			}
		}

		private void RemoveFromRenderersList(MeshRenderer renderer)
		{
			if ((bool)renderer)
			{
				_renderers.Remove(renderer);
			}
		}
	}
}
