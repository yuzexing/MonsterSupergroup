using UnityEngine;

namespace AstralShift.Helpers
{
	public static class SpriteHelpers
	{
		public static Vector2 UVToScale(Vector2[] uv)
		{
			return uv[1] - uv[2];
		}

		public static Vector2 UVToOffset(Vector2[] uv)
		{
			return uv[2];
		}

		public static void SetTextureWithAtlasSupport(Sprite sprite, Material material, int propID)
		{
			if ((bool)sprite)
			{
				material.SetTexture(propID, sprite.texture);
				if (sprite.packed)
				{
					material.SetTextureScale(propID, UVToScale(sprite.uv));
					material.SetTextureOffset(propID, UVToOffset(sprite.uv));
				}
			}
		}
	}
}
