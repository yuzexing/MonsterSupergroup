using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.Rendering
{
	public class MaterialGreyScaleManager : MonoBehaviour
	{
		[SerializeField]
		private MaterialGreyScaleValueLUT greyScaleValueLut;

		private HashSet<MaterialGreyScaleSprite> _spritesHashSet = new HashSet<MaterialGreyScaleSprite>();

		public static MaterialGreyScaleManager Instance { get; private set; }

		private void Start()
		{
			Instance = this;
		}

		private void OnDestroy()
		{
			Instance = null;
		}

		public void RegisterGreyScaleSprite(MaterialGreyScaleSprite sprite)
		{
			_spritesHashSet.Add(sprite);
		}

		public void UnRegisterGreyScaleSprite(MaterialGreyScaleSprite sprite)
		{
			_spritesHashSet.Remove(sprite);
		}

		public int GetGreyScaleValueFromPosition(Vector2 position)
		{
			int greyscale = -1;
			int num = int.MinValue;
			foreach (MaterialGreyScaleSprite item in _spritesHashSet)
			{
				int greyScaleValueFromPosition = item.GetGreyScaleValueFromPosition(position);
				if (greyScaleValueFromPosition >= 0 && item.Priority >= num)
				{
					num = item.Priority;
					greyscale = greyScaleValueFromPosition;
				}
			}
			return greyScaleValueLut.GetMaterialValueFromGreyscale(greyscale);
		}
	}
}
