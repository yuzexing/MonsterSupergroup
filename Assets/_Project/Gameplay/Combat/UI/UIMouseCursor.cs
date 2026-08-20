using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI
{
	public class UIMouseCursor : UIBehaviour
	{
		[SerializeField]
		private Image image;

		[SerializeField]
		private CanvasGroup canvasGroup;

		[SerializeField]
		private Sprite defaultCursor;

		[SerializeField]
		[Range(0f, 1f)]
		private float defaultSpriteSaturationThreshold = 1f;

		[SerializeField]
		private Sprite clickCursor;

		[SerializeField]
		[Range(0f, 1f)]
		private float clickSpriteSaturationThreshold = 1f;

		[SerializeField]
		private float defaultScale = 1f;

		private Material _cursorMaterial;

		private float _currentHue;

		private float _currentSaturation = 1f;

		private float _currentThreshold;

		private static readonly int HueShiftProp = Shader.PropertyToID("_HueShift");

		private static readonly int TintColorProp = Shader.PropertyToID("_Color");

		private static readonly int SatProp = Shader.PropertyToID("_SaturationThreshold");

		protected override void Awake()
		{
			base.Awake();
			if ((bool)image)
			{
				_cursorMaterial = new Material(image.materialForRendering);
				image.material = _cursorMaterial;
			}
			_currentThreshold = defaultSpriteSaturationThreshold;
		}

		public void SetState(UIMouseCursorHandler.CursorState state)
		{
			switch (state)
			{
			case UIMouseCursorHandler.CursorState.Default:
				if ((bool)defaultCursor)
				{
					image.sprite = defaultCursor;
					_currentThreshold = defaultSpriteSaturationThreshold;
				}
				break;
			case UIMouseCursorHandler.CursorState.Clicked:
				if ((bool)clickCursor)
				{
					image.sprite = clickCursor;
					_currentThreshold = clickSpriteSaturationThreshold;
				}
				break;
			}
			UpdateShader();
		}

		public void SetScale(float multiplier)
		{
			base.transform.localScale = Vector2.one * (defaultScale * multiplier);
		}

		public void SetHue(float hue, float saturation)
		{
			_currentHue = hue;
			_currentSaturation = saturation;
			UpdateShader();
		}

		private void UpdateShader()
		{
			if ((bool)_cursorMaterial)
			{
				Color value = Color.HSVToRGB(_currentHue, _currentSaturation, 1f);
				_cursorMaterial.SetColor(TintColorProp, value);
				_cursorMaterial.SetFloat(HueShiftProp, _currentHue);
				_cursorMaterial.SetFloat(SatProp, _currentThreshold);
			}
		}

		public void Show()
		{
			canvasGroup.alpha = 1f;
		}

		public void Hide()
		{
			canvasGroup.alpha = 0f;
		}
	}
}
