using UnityEngine;

namespace AstralShift.Rendering.Shaders.WindShader
{
	[RequireComponent(typeof(SpriteRenderer))]
	public class WindSeed : MonoBehaviour
	{
		private SpriteRenderer _spriteRenderer;

		private MaterialPropertyBlock _mpb;

		private void Awake()
		{
			_spriteRenderer = GetComponent<SpriteRenderer>();
			_mpb = new MaterialPropertyBlock();
			_spriteRenderer.GetPropertyBlock(_mpb);
			_mpb.SetFloat("_Seed", Random.Range(0f, 1f));
			_spriteRenderer.SetPropertyBlock(_mpb);
		}
	}
}
