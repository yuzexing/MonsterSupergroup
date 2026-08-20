using System;
using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[Serializable]
	public class CardVisualLayerGroup
	{
		[SerializeField]
		protected CardVisualLayer main;

		[SerializeField]
		protected List<CardVisualLayer> additional;

		public CardVisualLayer Main => main;

		public List<CardVisualLayer> Additional => additional;
	}
}
