using AstralShift.HellMaiden.MapGeneration;
using AstralShift.Managers;
using UnityEngine;

namespace AstralShift.Rendering
{
	public class Occludee : MonoBehaviour
	{
		public SpriteRenderer[] renderers;

		public Bounds bounds;

		private Transform _transform;

		private readonly int _occluderOnShaderPropID = Shader.PropertyToID("_OccluderOn");

		private readonly int _occludeeBoundsMinShaderPropID = Shader.PropertyToID("_OccludeeBounds");

		private readonly int _occluderDistanceId = Shader.PropertyToID("_OccluderDistance");

		private readonly int _occluderSoftnessId = Shader.PropertyToID("_OccluderSoftness");

		private readonly int _occluderMinAlphaId = Shader.PropertyToID("_OccluderMinAlpha");

		protected void OnEnable()
		{
			SpriteRenderer[] array = renderers;
			foreach (SpriteRenderer obj in array)
			{
				obj.material.SetFloat(_occluderOnShaderPropID, 1f);
				obj.material.SetFloat(_occluderDistanceId, OccludeeManager.Instance.FadeDistance);
				obj.material.SetFloat(_occluderSoftnessId, OccludeeManager.Instance.FadeSoftness);
				obj.material.SetFloat(_occluderMinAlphaId, OccludeeManager.Instance.FadeMinAlpha);
			}
			MapGenerator.OnTilesMoved += UpdateBounds;
			UpdateBounds();
		}

		protected virtual void OnDisable()
		{
			MapGenerator.OnTilesMoved -= UpdateBounds;
			SpriteRenderer[] array = renderers;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].material.SetFloat(_occluderOnShaderPropID, 0f);
			}
		}

		private void UpdateBounds(TileGenerator[] tileGenerators = null, MapGenerator mapGenerator = null)
		{
			SpriteRenderer[] array = renderers;
			foreach (SpriteRenderer obj in array)
			{
				Bounds bounds = GetBounds();
				obj.material.SetVector(_occludeeBoundsMinShaderPropID, new Vector4(bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y));
			}
		}

		private Bounds GetBounds()
		{
			Bounds result = bounds;
			if (!_transform && !TryGetComponent<Transform>(out _transform))
			{
				result.center += new Vector3(10000f, 10000f, 0f);
				return result;
			}
			result.center += _transform.position;
			result.size = Vector3.Scale(bounds.size, _transform.lossyScale);
			return result;
		}

		public virtual void CalculateBounds()
		{
			if (renderers == null || renderers.Length == 0)
			{
				return;
			}
			bounds = default(Bounds);
			for (int i = 0; i < renderers.Length; i++)
			{
				if (!(renderers[i] == null))
				{
					bounds.Encapsulate(renderers[i].bounds);
				}
			}
		}
	}
}
