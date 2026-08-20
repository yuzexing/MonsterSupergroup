using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AstralShift.Rendering
{
	[CreateAssetMenu(fileName = "MaterialGreyScaleValueLUT", menuName = "AstralShift/Rendering/Grey Scale to Value LUT")]
	public class MaterialGreyScaleValueLUT : ScriptableObject
	{
		[Serializable]
		public struct GreyscaleValue
		{
			public int value;

			public MaterialValue materialValue;
		}

		public enum MaterialValue
		{
			Grass = 0,
			Stone = 1,
			Water = 2,
			Dirt = 3
		}

		[SerializeField]
		private List<GreyscaleValue> values = new List<GreyscaleValue>();

		public int GetMaterialValueFromGreyscale(int greyscale)
		{
			using (IEnumerator<GreyscaleValue> enumerator = values.Where((GreyscaleValue greyscaleValue) => greyscaleValue.value == greyscale).GetEnumerator())
			{
				if (enumerator.MoveNext())
				{
					return (int)enumerator.Current.materialValue;
				}
			}
			return -1;
		}
	}
}
