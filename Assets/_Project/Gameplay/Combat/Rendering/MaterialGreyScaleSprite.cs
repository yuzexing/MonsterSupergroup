using UnityEngine;

namespace AstralShift.Rendering
{
	public class MaterialGreyScaleSprite : MonoBehaviour
	{
		[SerializeField]
		private SpriteRenderer spriteRenderer;

		[SerializeField]
		private int priority;

		private sbyte[] _cachedValues;

		private int _textureWidth;

		private int _textureHeight;

		private float _pixelsPerUnit;

		private Vector2 _pivot;

		private Transform _spriteTransform;

		public int Priority => priority;

		private void Awake()
		{
			if ((bool)spriteRenderer)
			{
				if (!spriteRenderer.sprite)
				{
					Debug.LogWarning("This background does not have a greyscale sprite renderer setup for materials.", this);
					return;
				}
				CacheTexturePixels();
				Object.Destroy(spriteRenderer);
			}
		}

		private void OnEnable()
		{
			MaterialGreyScaleManager.Instance?.RegisterGreyScaleSprite(this);
		}

		private void OnDisable()
		{
			MaterialGreyScaleManager.Instance?.UnRegisterGreyScaleSprite(this);
		}

		private void OnDestroy()
		{
			MaterialGreyScaleManager.Instance?.UnRegisterGreyScaleSprite(this);
		}

		private void CacheTexturePixels()
		{
			Sprite sprite = spriteRenderer.sprite;
			if (!sprite || !sprite.texture)
			{
				return;
			}
			Texture2D texture = sprite.texture;
			_textureWidth = Mathf.RoundToInt(sprite.textureRect.width);
			_textureHeight = Mathf.RoundToInt(sprite.textureRect.height);
			_pixelsPerUnit = sprite.pixelsPerUnit;
			_pivot = sprite.pivot;
			_spriteTransform = spriteRenderer.transform;
			int num = Mathf.RoundToInt(sprite.textureRect.x);
			int num2 = Mathf.RoundToInt(sprite.textureRect.y);
			RenderTexture temporary = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
			Graphics.Blit(texture, temporary);
			RenderTexture active = RenderTexture.active;
			RenderTexture.active = temporary;
			Texture2D texture2D = new Texture2D(_textureWidth, _textureHeight, TextureFormat.RGBA32, mipChain: false);
			texture2D.ReadPixels(new Rect(num, num2, _textureWidth, _textureHeight), 0, 0);
			texture2D.Apply();
			Color[] pixels = texture2D.GetPixels();
			RenderTexture.active = active;
			RenderTexture.ReleaseTemporary(temporary);
			Object.Destroy(texture2D);
			_cachedValues = new sbyte[pixels.Length];
			for (int i = 0; i < pixels.Length; i++)
			{
				if (pixels[i].a == 0f)
				{
					_cachedValues[i] = -1;
				}
				else
				{
					_cachedValues[i] = (sbyte)Mathf.RoundToInt(pixels[i].b * 100f);
				}
			}
		}

		public int GetGreyScaleValueFromPosition(Vector2 worldPosition)
		{
			if (!_spriteTransform || _cachedValues == null)
			{
				return -1;
			}
			Vector3 vector = _spriteTransform.InverseTransformPoint(worldPosition);
			int num = Mathf.RoundToInt(vector.x * _pixelsPerUnit + _pivot.x);
			int num2 = Mathf.RoundToInt(vector.y * _pixelsPerUnit + _pivot.y);
			if (num < 0 || num >= _textureWidth || num2 < 0 || num2 >= _textureHeight)
			{
				return -1;
			}
			int num3 = num2 * _textureWidth + num;
			if (num3 < 0 || num3 >= _cachedValues.Length)
			{
				return -1;
			}
			sbyte b = _cachedValues[num3];
			if (b == -1)
			{
				return -1;
			}
			return b;
		}
	}
}
