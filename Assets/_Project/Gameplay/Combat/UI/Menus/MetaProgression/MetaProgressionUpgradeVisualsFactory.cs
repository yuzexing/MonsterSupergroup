using Assets.Scripts.AstralShift.HellMaiden.Data;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Menus.MetaProgression
{
	public static class MetaProgressionUpgradeVisualsFactory
	{
		private static PlayerMetaStatsDatabase _metaStatsDatabase;

		public static async UniTask<MetaProgressionUpgradeIconView> GetMetaProgressionIconView(MetaStatDatabaseEntry entry, bool isMaxLevel = false)
		{
			if (!_metaStatsDatabase)
			{
				_metaStatsDatabase = GameDirector.Instance.runtimeDB.MetaStatsDB;
			}
			AsyncInstantiateOperation<MetaProgressionUpgrade3DIcon> default3DIconInstantiateOperation = Object.InstantiateAsync(_metaStatsDatabase.GetUpgradeIcon3DPrefab(isMaxLevel: false));
			AsyncInstantiateOperation<MetaProgressionUpgrade3DIcon> maxedOut3DIconInstantiateOperation = Object.InstantiateAsync(_metaStatsDatabase.GetUpgradeIcon3DPrefab(isMaxLevel: true));
			AsyncInstantiateOperation<MetaProgressionUpgradeIconView> iconViewInstantiateOperation = Object.InstantiateAsync(_metaStatsDatabase.GetUpgradeIconViewPrefab());
			await UniTask.WhenAll(default3DIconInstantiateOperation.ToUniTask(), maxedOut3DIconInstantiateOperation.ToUniTask(), iconViewInstantiateOperation.ToUniTask());
			MetaProgressionUpgrade3DIcon metaProgressionUpgrade3DIcon = default3DIconInstantiateOperation.Result[0];
			MetaProgressionUpgrade3DIcon metaProgressionUpgrade3DIcon2 = maxedOut3DIconInstantiateOperation.Result[0];
			MetaProgressionUpgradeIconView metaProgressionUpgradeIconView = iconViewInstantiateOperation.Result[0];
			if (!metaProgressionUpgradeIconView || !metaProgressionUpgrade3DIcon || !metaProgressionUpgrade3DIcon2)
			{
				Debug.LogError("MetaProgressionUpgradeVisualsFactory: Could not generate the MetaProgressionIconView. Invalid Template!");
				return null;
			}
			Material mainGemMaterial = _metaStatsDatabase.GetMainGemMaterial(entry.color);
			Material smallGemMaterial = _metaStatsDatabase.GetSmallGemMaterial(entry.color);
			metaProgressionUpgrade3DIcon.SetIcon(entry.icon);
			metaProgressionUpgrade3DIcon.SetGemMaterials(mainGemMaterial, smallGemMaterial);
			metaProgressionUpgrade3DIcon2.SetIcon(entry.icon);
			metaProgressionUpgrade3DIcon2.SetGemMaterials(mainGemMaterial, smallGemMaterial);
			metaProgressionUpgradeIconView.Initialize(metaProgressionUpgrade3DIcon, metaProgressionUpgrade3DIcon2);
			return metaProgressionUpgradeIconView;
		}

		public static MetaProgressionUpgrade3DIcon GetMetaProgression3DIcon(MetaStatDatabaseEntry entry, bool isMaxLevel = false)
		{
			if (!_metaStatsDatabase)
			{
				_metaStatsDatabase = GameDirector.Instance.runtimeDB.MetaStatsDB;
			}
			MetaProgressionUpgrade3DIcon metaProgressionUpgrade3DIcon = Object.Instantiate(_metaStatsDatabase.GetUpgradeIcon3DPrefab(isMaxLevel));
			if (!metaProgressionUpgrade3DIcon)
			{
				Debug.LogError("MetaProgressionUpgradeVisualsFactory: Could not generate the MetaProgressionUpgrade3DIcon. Invalid Template!");
				return null;
			}
			metaProgressionUpgrade3DIcon.SetIcon(entry.icon);
			Material mainGemMaterial = _metaStatsDatabase.GetMainGemMaterial(entry.color);
			Material smallGemMaterial = _metaStatsDatabase.GetSmallGemMaterial(entry.color);
			metaProgressionUpgrade3DIcon.SetGemMaterials(mainGemMaterial, smallGemMaterial);
			return metaProgressionUpgrade3DIcon;
		}
	}
}
