using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data.Perks;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Perks
{
	public static class PerkVisualsFactory
	{
		private static PerkTemplateLUT _templateLUT;

		public static async UniTask<PerkView> GetUIPerk(RuntimePerkData runtimeData, Transform parent = null)
		{
			if (!_templateLUT)
			{
				_templateLUT = GameDirector.Instance.runtimeDB.PerkDB.VisualsTemplateLUT;
			}
			PerkRarity rarity = runtimeData.Rarity;
			AsyncInstantiateOperation instantiateOperation = Object.InstantiateAsync(_templateLUT.UIPerkViewTemplate, parent);
			await instantiateOperation;
			Object obj = instantiateOperation.Result[0];
			if (!(obj is PerkView newPerkView))
			{
				Debug.LogError("PerkVisualsFactory: Could not generate the Perk. Invalid Template!");
				return null;
			}
			instantiateOperation = Object.InstantiateAsync(_templateLUT.PerRarity3DTemplates[(int)rarity]);
			await instantiateOperation;
			obj = instantiateOperation.Result[0];
			if (!(obj is Perk3DView newPerk3DView))
			{
				Debug.LogError("PerkVisualsFactory: Could not generate the 3D Perk. Invalid Template!");
				return null;
			}
			newPerkView.Initialize(runtimeData);
			UIPerkRenderingManager.Instance.AddPerk(newPerkView, newPerk3DView);
			newPerkView.SetTitle(runtimeData.Data.GetTitle());
			newPerkView.SetDescription(runtimeData.Data.GetDescription(runtimeData.Rarity));
			newPerkView.SetIcon(runtimeData.Data.GetIcon());
			newPerkView.SetGlowRarity(runtimeData.Rarity);
			SetLevelAndStatsChange(newPerkView, runtimeData);
			await UniTask.NextFrame();
			await UniTask.NextFrame();
			UIPerkRenderingManager.Instance.TryCacheStaticTexture(runtimeData, newPerk3DView);
			await UniTask.NextFrame();
			return newPerkView;
		}

		private static void SetLevelAndStatsChange(PerkView perkView, RuntimePerkData runtimeData)
		{
			int level = 1;
			RuntimePerk runtimePerk = null;
			if (PlayerHand.Instance.TryGetAllPerks(out var perks))
			{
				runtimePerk = perks.Find((RuntimePerk element) => element.RuntimeData.Data.ID == runtimeData.Data.ID);
				if (runtimePerk != null)
				{
					level = runtimePerk.Level + 1;
				}
			}
			perkView.SetLevel(level);
			perkView.SetStatChangeInfo(runtimeData, runtimePerk);
		}
	}
}
