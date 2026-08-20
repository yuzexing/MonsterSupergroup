using UnityEngine;

namespace AstralShift.Rendering.Shaders
{
	[ExecuteInEditMode]
	public class Pixelation : MonoBehaviour
	{
		public Material effectMaterial;

		private void OnRenderImage(RenderTexture source, RenderTexture destination)
		{
			Graphics.Blit(source, destination, effectMaterial);
		}
	}
}
