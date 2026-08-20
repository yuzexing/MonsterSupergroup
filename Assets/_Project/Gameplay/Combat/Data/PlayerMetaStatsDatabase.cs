using System.Collections.Generic;
using AstralShift.HellMaiden.UI.Menus.MetaProgression;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Assets.Scripts.AstralShift.HellMaiden.Data
{
	[CreateAssetMenu(fileName = "New MetaStatsDB", menuName = "HellMaiden/Data/Player/MetaStatsDB")]
	public class PlayerMetaStatsDatabase : SerializedScriptableObject
	{
		[Header("Templates")]
		[SerializeField]
		private MetaProgressionUpgradeIconView upgradeIconView;

		[SerializeField]
		private MetaProgressionUpgrade3DIcon baseUpgradeIcon;

		[SerializeField]
		private MetaProgressionUpgrade3DIcon maxUpgradeIcon;

		[SerializeField]
		private Dictionary<MetaColor, MetaColorEntry> colorLUT;

		[Header("Entries")]
		public Dictionary<MetaProgressionID, MetaStatDatabaseEntry> entries;

		public MetaProgressionUpgradeIconView GetUpgradeIconViewPrefab()
		{
			return upgradeIconView;
		}

		public MetaProgressionUpgrade3DIcon GetUpgradeIcon3DPrefab(bool isMaxLevel)
		{
			if (!isMaxLevel)
			{
				return baseUpgradeIcon;
			}
			return maxUpgradeIcon;
		}

		public Material GetMainGemMaterial(MetaColor color)
		{
			if (!colorLUT.TryGetValue(color, out var value))
			{
				return null;
			}
			return value.MainGemMaterial;
		}

		public Material GetSmallGemMaterial(MetaColor color)
		{
			if (!colorLUT.TryGetValue(color, out var value))
			{
				return null;
			}
			return value.SmallGemMaterial;
		}
	}
}
