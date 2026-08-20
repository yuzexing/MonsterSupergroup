using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[CreateAssetMenu(fileName = "New Weapon Card Visual Data", menuName = "HellMaiden/Data/Cards/Visuals/Weapon Card Visual Data")]
	public class CardVisualData : ScriptableObject
	{
		[Space]
		[SerializeField]
		protected CardVisualLayer illustration;

		[SerializeField]
		protected List<CardVisualLayer> illustrationLayers;

		[Space]
		[SerializeField]
		protected List<CardVisualLayer> foregroundLayers;

		[Space]
		[SerializeField]
		protected CardVisualLayer textBoxBackground;

		public CardVisualLayer Illustration => illustration;

		public List<CardVisualLayer> IllustrationLayers => illustrationLayers;

		public List<CardVisualLayer> ForegroundLayers => foregroundLayers;

		public CardVisualLayer TextBoxBackground => textBoxBackground;
	}
}
