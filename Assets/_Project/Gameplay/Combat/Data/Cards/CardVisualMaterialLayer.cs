using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[Serializable]
	public class CardVisualMaterialLayer : CardVisualLayer
	{
		[SerializeField]
		protected Material _material;

		public new Material Material => _material;
	}
}
