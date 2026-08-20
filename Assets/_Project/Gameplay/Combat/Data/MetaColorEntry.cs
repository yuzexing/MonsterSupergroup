using System;
using UnityEngine;

namespace Assets.Scripts.AstralShift.HellMaiden.Data
{
	[Serializable]
	public class MetaColorEntry
	{
		[SerializeField]
		private Material mainGemMaterial;

		[SerializeField]
		private Material smallGemMaterial;

		public Material MainGemMaterial => mainGemMaterial;

		public Material SmallGemMaterial => smallGemMaterial;
	}
}
