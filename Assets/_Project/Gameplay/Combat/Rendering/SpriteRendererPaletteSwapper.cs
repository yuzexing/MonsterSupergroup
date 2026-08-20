using System.Collections.Generic;
using AstralShift.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.Rendering
{
	[DefaultExecutionOrder(30000)]
	[RequireComponent(typeof(SpriteRenderer))]
	public class SpriteRendererPaletteSwapper : MonoBehaviour
	{
		public const int DefaultExecutionOrder = 30000;

		[SerializeField]
		[Tooltip("The SpriteRenderer that will have its Sprite modified")]
		private SpriteRenderer _Renderer;

		private Texture2D _previousTexture;

		[Tooltip("The replacement for the original Sprite texture")]
		[SerializeField]
		[ReadOnly]
		private Texture2D _modifiedTexture;

		private Texture2D _colorLut;

		private PaletteSwapSpriteManager.ColorLookupTextureMap _mainMap;

		private Dictionary<Sprite, Sprite> _spriteMap;

		private bool _isEnabled;

		public ref SpriteRenderer Renderer => ref _Renderer;

		public Texture2D ModifiedTexture
		{
			get
			{
				return _modifiedTexture;
			}
			set
			{
				_modifiedTexture = value;
				RefreshSpriteMap();
			}
		}

		public Texture2D ColorLut
		{
			get
			{
				return _colorLut;
			}
			set
			{
				_colorLut = value;
				_isEnabled = _colorLut;
				RefreshColorLutMap();
			}
		}

		protected virtual void Awake()
		{
			if (_Renderer == null)
			{
				TryGetComponent<SpriteRenderer>(out _Renderer);
			}
		}

		protected virtual void LateUpdate()
		{
			if (_isEnabled && !(_Renderer == null))
			{
				TrySwapTexture();
				Sprite sprite = _Renderer.sprite;
				if (TrySwapSprite(_modifiedTexture, _spriteMap, ref sprite))
				{
					_Renderer.sprite = sprite;
				}
			}
		}

		private void RefreshColorLutMap()
		{
			if ((bool)ColorLut)
			{
				_mainMap = PaletteSwapSpriteManager.GetOrCreateColorLutTextureMap(ColorLut);
			}
		}

		private void RefreshSpriteMap()
		{
			_spriteMap = _mainMap.GetSpriteMap(_modifiedTexture);
		}

		private void TrySwapTexture()
		{
			if (_previousTexture != _Renderer.sprite.texture)
			{
				Texture2D texture = _Renderer.sprite.texture;
				if (!_mainMap.TryGetModifiedTexture(texture, out var modifiedTexture) && !_mainMap.IsAModifiedTexture(texture))
				{
					modifiedTexture = PaletteSwapSpriteManager.CopyTextureAndApplyPalette(texture, ColorLut);
					_mainMap.TryAddModifiedTexture(texture, modifiedTexture);
				}
				_previousTexture = _Renderer.sprite.texture;
				ModifiedTexture = modifiedTexture;
			}
		}

		public static bool TrySwapSprite(Texture2D modifiedTexture, Dictionary<Sprite, Sprite> spriteMap, ref Sprite sprite)
		{
			if (spriteMap == null || sprite == null || modifiedTexture == null || sprite.texture == modifiedTexture)
			{
				return false;
			}
			if (!spriteMap.TryGetValue(sprite, out var value))
			{
				value = PaletteSwapSpriteManager.SliceModifiedTexture(sprite, modifiedTexture);
				spriteMap.Add(sprite, value);
			}
			sprite = value;
			return true;
		}

		private void OnDestroy()
		{
			if (_mainMap != null)
			{
				_mainMap.Clear();
			}
		}
	}
}
