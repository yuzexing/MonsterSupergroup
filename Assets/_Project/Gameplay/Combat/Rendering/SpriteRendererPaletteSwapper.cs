using System.Collections.Generic;
using AstralShift.Helpers.Attributes;
using UnityEngine;
using MonsterSupergroup.Gameplay.Combat.Content;

namespace AstralShift.Rendering
{
	[DefaultExecutionOrder(30000)]
	[RequireComponent(typeof(SpriteRenderer))]
	public class SpriteRendererPaletteSwapper : MonoBehaviour
	{
        [System.Serializable]
        public struct BakedPalette { public Texture2D original, lut, baked; }
        [SerializeField, HideInInspector] private BakedPalette[] bakedPalettes = System.Array.Empty<BakedPalette>();
        private Sprite _originalSprite;
        private EnemyAppearanceDefinition _appearance;
        private EnemyAppearanceCache.Lease _appearanceLease;
        public EnemyAppearanceDefinition Appearance => _appearance;
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
                return _appearance != null ? _appearance.Lut : _colorLut;
			}
			set
			{
                ReleaseAppearance();
				if (_colorLut == value) return;
                if (_Renderer != null && _Renderer.sprite != null && _Renderer.sprite.texture == _modifiedTexture && _originalSprite != null)
                    _Renderer.sprite = _originalSprite;
                PaletteSwapSpriteManager.Release(_colorLut);
				_previousTexture = null;
				_modifiedTexture = null;
				_spriteMap = null;
				_colorLut = value;
                PaletteSwapSpriteManager.Retain(_colorLut);
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
            RegisterBakedPalettes();
		}

        private void RegisterBakedPalettes()
        {
            if (ColorLut == null) return;
            foreach (var pair in bakedPalettes)
                if (pair.lut == ColorLut)
                    PaletteSwapSpriteManager.RegisterBakedTexture(pair.original, pair.lut, pair.baked);
        }

		protected virtual void LateUpdate()
		{
            if (_appearanceLease != null && _Renderer != null && _Renderer.sprite != null)
            {
                _originalSprite = _appearanceLease.Original(_Renderer.sprite);
                _Renderer.sprite = _appearanceLease.Map(_originalSprite);
                _modifiedTexture = _Renderer.sprite.texture;
                return;
            }
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
            RegisterBakedPalettes();
            _mainMap = ColorLut ? PaletteSwapSpriteManager.GetOrCreateColorLutTextureMap(ColorLut) : null;
		}

		private void RefreshSpriteMap()
		{
			_spriteMap = _mainMap?.GetSpriteMap(_modifiedTexture);
		}

		private void TrySwapTexture()
		{
            if (_Renderer.sprite == null) return;
            if (_modifiedTexture != null && _Renderer.sprite.texture == _modifiedTexture) return;
            if (_Renderer.sprite.texture != _modifiedTexture) _originalSprite = _Renderer.sprite;
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

		// The map belongs to the shared manager. One enemy's death must not clear
		// sprites/textures still referenced by other living enemies of this variant.
        public void ApplyAppearance(EnemyAppearanceDefinition value)
        {
            if (value == null) throw new System.ArgumentNullException(nameof(value));
            if (_appearance == value && _appearanceLease != null) return;
            ReleaseAppearance();
            if (_Renderer != null && _Renderer.sprite != null && _Renderer.sprite.texture == _modifiedTexture && _originalSprite != null)
                _Renderer.sprite = _originalSprite;
            PaletteSwapSpriteManager.Release(_colorLut);
            _colorLut = null; _isEnabled = false; _mainMap = null; _spriteMap = null;
            _previousTexture = null; _modifiedTexture = null;
            _appearanceLease = EnemyAppearanceCache.Acquire(value); _appearance = value;
        }
        private void ReleaseAppearance()
        {
            if (_appearanceLease == null) return;
            if (_Renderer != null) _Renderer.sprite = _appearanceLease.Original(_Renderer.sprite);
            _appearanceLease.Dispose(); _appearanceLease = null; _appearance = null;
            _modifiedTexture = null; _previousTexture = null;
        }
        protected virtual void OnDestroy()
        { ReleaseAppearance(); PaletteSwapSpriteManager.Release(_colorLut); }
	}
}
