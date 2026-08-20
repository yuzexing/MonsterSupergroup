using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[Serializable]
	public class CardVisualLayer
	{
		[SerializeField]
		protected Sprite sprite;

		[SerializeField]
		protected Material material;

		public Sprite Sprite => sprite;

		public Material Material => material;
	}
}
