using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[CreateAssetMenu(fileName = "New Weapon Template Visual Data", menuName = "HellMaiden/Data/Cards/Visuals/Weapon Template Visual Data")]
	public class WeaponTemplateVisualData : CardTemplateVisualData
	{
		[SerializeField]
		protected CardVisualMaterialLayer silverFrame;

		[SerializeField]
		protected CardVisualMaterialLayer goldFrame;

		[SerializeField]
		protected CardVisualMaterialLayer holographicFrame;

		[SerializeField]
		protected Material silverSelectionGlow;

		[SerializeField]
		protected Material goldSelectionGlow;

		[SerializeField]
		protected Material holographicSelectionGlow;

		[SerializeField]
		protected CardVisualLayer textBoxBackground;

		[SerializeField]
		protected Color32 textColor = new Color32(0, 0, 0, byte.MaxValue);

		[SerializeField]
		protected Color32 quoteColor = new Color32(100, 100, 100, byte.MaxValue);

		[SerializeField]
		protected Color32 quoteSeparatorColor = new Color32(100, 100, 100, byte.MaxValue);

		public CardVisualMaterialLayer SilverFrame => silverFrame;

		public CardVisualMaterialLayer GoldFrame => goldFrame;

		public CardVisualMaterialLayer HolographicFrame => holographicFrame;

		public CardVisualLayer TextBoxBackground => textBoxBackground;

		public Color32 TextColor => textColor;

		public Color32 QuoteColor => quoteColor;

		public Color32 QuoteSeparatorColor => quoteSeparatorColor;

		public CardVisualMaterialLayer GetFrame(WeaponRarity rarity)
		{
			return rarity switch
			{
				WeaponRarity.Silver => silverFrame, 
				WeaponRarity.Gold => goldFrame, 
				WeaponRarity.Holographic => holographicFrame, 
				_ => silverFrame, 
			};
		}

		public Material GetSelectionGlow(WeaponRarity rarity)
		{
			return rarity switch
			{
				WeaponRarity.Silver => silverSelectionGlow, 
				WeaponRarity.Gold => goldSelectionGlow, 
				WeaponRarity.Holographic => holographicSelectionGlow, 
				_ => silverSelectionGlow, 
			};
		}
	}
}
