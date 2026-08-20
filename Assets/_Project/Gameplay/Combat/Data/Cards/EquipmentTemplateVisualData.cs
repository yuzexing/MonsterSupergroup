using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[CreateAssetMenu(fileName = "New Equipment Template Visual Data", menuName = "HellMaiden/Data/Cards/Visuals/Equipment Template Visual Data")]
	public class EquipmentTemplateVisualData : CardTemplateVisualData
	{
		[Serializable]
		public class CardEffects
		{
			public Material selectionGlow;
		}

		[SerializeField]
		protected CardVisualLayer frameBackground;

		[Space]
		[SerializeField]
		protected CardVisualLayer[] framesPerLevel;

		[Space]
		[SerializeField]
		protected CardVisualLayer[] levelIcons;

		[Space]
		[SerializeField]
		protected CardEffects[] effectsPerLevel;

		[Space]
		[SerializeField]
		protected CardVisualLayer textBackground;

		[SerializeField]
		protected Color32 textColor = new Color32(0, 0, 0, byte.MaxValue);

		[SerializeField]
		protected Color32 quoteColor = new Color32(100, 100, 100, byte.MaxValue);

		[SerializeField]
		protected Color32 quoteSeparatorColor = new Color32(100, 100, 100, byte.MaxValue);

		public CardVisualLayer FrameBackground => frameBackground;

		public CardVisualLayer[] FramesPerLevel => framesPerLevel;

		public CardVisualLayer[] LevelIcons => levelIcons;

		public CardEffects[] EffectsPerLevel => effectsPerLevel;

		public CardVisualLayer TextBackground => textBackground;

		public Color32 TextColor => textColor;

		public Color32 QuoteColor => quoteColor;

		public Color32 QuoteSeparatorColor => quoteSeparatorColor;
	}
}
