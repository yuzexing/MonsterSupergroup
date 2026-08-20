using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.Rendering
{
	public static class PaletteSwapSpriteManager
	{
		public class ColorLookupTextureMap
		{
			private readonly Dictionary<Texture2D, Texture2D> _originalToModifiedTexturesMap = new Dictionary<Texture2D, Texture2D>();

			private readonly Dictionary<Texture2D, Dictionary<Sprite, Sprite>> _textureToSpriteMap = new Dictionary<Texture2D, Dictionary<Sprite, Sprite>>();

			public bool TryGetModifiedTexture(Texture2D originalTexture, out Texture2D modifiedTexture)
			{
				return _originalToModifiedTexturesMap.TryGetValue(originalTexture, out modifiedTexture);
			}

			public bool TryAddModifiedTexture(Texture2D originalTexture, Texture2D modifiedTexture)
			{
				return _originalToModifiedTexturesMap.TryAdd(originalTexture, modifiedTexture);
			}

			public bool IsAModifiedTexture(Texture2D texture)
			{
				return _originalToModifiedTexturesMap.ContainsValue(texture);
			}

			public Dictionary<Sprite, Sprite> GetSpriteMap(Texture2D modifiedTexture)
			{
				if (modifiedTexture == null)
				{
					return null;
				}
				if (!_textureToSpriteMap.TryGetValue(modifiedTexture, out var value))
				{
					_textureToSpriteMap.Add(modifiedTexture, value = new Dictionary<Sprite, Sprite>());
				}
				return value;
			}

			public void DestroySprites(Dictionary<Sprite, Sprite> spriteMap)
			{
				if (spriteMap == null)
				{
					return;
				}
				foreach (Sprite value in spriteMap.Values)
				{
					Object.Destroy(value);
				}
				spriteMap.Clear();
			}

			public void DestroySprites(Texture2D texture)
			{
				if (_textureToSpriteMap.Remove(texture, out var value))
				{
					DestroySprites(value);
				}
			}

			public void Clear()
			{
				if (_originalToModifiedTexturesMap.Count == 0)
				{
					return;
				}
				foreach (Texture2D value in _originalToModifiedTexturesMap.Values)
				{
					DestroySprites(value);
				}
				_textureToSpriteMap.Clear();
				List<Texture2D> list = new List<Texture2D>(_originalToModifiedTexturesMap.Values);
				for (int num = list.Count - 1; num >= 0; num--)
				{
					if (!Application.isPlaying)
					{
						Object.DestroyImmediate(list[num]);
					}
					else
					{
						Object.Destroy(list[num]);
					}
				}
				_originalToModifiedTexturesMap.Clear();
			}
		}

		private static Material _blitMaterial;

		private static readonly int _ColorLookupTexPropID = Shader.PropertyToID("_ColorLookupTex");

		private static readonly Dictionary<Texture2D, ColorLookupTextureMap> GlobalMap = new Dictionary<Texture2D, ColorLookupTextureMap>();

		public static Material BlitMaterial
		{
			get
			{
				if (_blitMaterial == null)
				{
					_blitMaterial = new Material(Shader.Find("AstralShift/PaletteSwapBlitShader"));
				}
				return _blitMaterial;
			}
		}

		public static Texture2D CopyTextureAndApplyPalette(Texture2D source, Texture2D colorLUT)
		{
			RenderTexture temporary = RenderTexture.GetTemporary(source.width, source.height, 0, source.graphicsFormat);
			temporary.useMipMap = true;
			RenderTexture active = RenderTexture.active;
			RenderTexture.active = temporary;
			BlitMaterial.SetTexture(_ColorLookupTexPropID, colorLUT);
			Graphics.Blit(source, temporary, BlitMaterial);
			Texture2D texture2D = new Texture2D(source.width, source.height, source.format, source.mipmapCount > 1);
			texture2D.name = source.name + "_Modified";
			texture2D.filterMode = source.filterMode;
			texture2D.anisoLevel = source.anisoLevel;
			texture2D.mipMapBias = source.mipMapBias;
			texture2D.wrapMode = source.wrapMode;
			texture2D.requestedMipmapLevel = source.requestedMipmapLevel;
			texture2D.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
			texture2D.Apply(updateMipmaps: true, makeNoLongerReadable: true);
			RenderTexture.active = active;
			RenderTexture.ReleaseTemporary(temporary);
			return texture2D;
		}

		public static Sprite SliceModifiedTexture(Sprite originalSprite, Texture2D modifiedTexture)
		{
			Vector2 pivot = originalSprite.pivot;
			if (originalSprite.packed)
			{
				pivot.x /= originalSprite.textureRect.width;
				pivot.y /= originalSprite.textureRect.height;
				return Sprite.Create(modifiedTexture, originalSprite.textureRect, pivot, originalSprite.pixelsPerUnit, 0u, SpriteMeshType.FullRect, originalSprite.border, generateFallbackPhysicsShape: false);
			}
			pivot.x /= originalSprite.rect.width;
			pivot.y /= originalSprite.rect.height;
			return Sprite.Create(modifiedTexture, originalSprite.rect, pivot, originalSprite.pixelsPerUnit, 0u, SpriteMeshType.FullRect, originalSprite.border, generateFallbackPhysicsShape: false);
		}

		public static ColorLookupTextureMap GetOrCreateColorLutTextureMap(Texture2D colorLut)
		{
			if (!colorLut)
			{
				return null;
			}
			if (GlobalMap.TryGetValue(colorLut, out var value))
			{
				return value;
			}
			value = new ColorLookupTextureMap();
			GlobalMap.TryAdd(colorLut, value);
			return value;
		}

		public static void ClearAll()
		{
			if (GlobalMap.Count == 0)
			{
				return;
			}
			foreach (ColorLookupTextureMap value in GlobalMap.Values)
			{
				value.Clear();
			}
		}
	}
}
