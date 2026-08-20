using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data.Perks;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace AstralShift.HellMaiden.UI
{
	public class EndStatsPerk : MonoBehaviour
	{
		[FormerlySerializedAs("cooldownCardVisuals")]
		[SerializeField]
		private UICardOrPerkStaticElement perkVisual;

		[SerializeField]
		private TextMeshProUGUI titleText;

		[SerializeField]
		private TextMeshProUGUI levelNumberTxt;

		[SerializeField]
		private GameObject levelSphere;

		[SerializeField]
		private GameObject levelTxt;

		[SerializeField]
		private RunStatsPerkEffectsInfo runStatsPerkEffectsInfoPrefab;

		[SerializeField]
		private GameObject runStatsPerkEffectsInfoContainer;

		public void SetPerkInfo(RuntimePerk runtimePerk)
		{
			if (runtimePerk == null)
			{
				SetInactive();
				return;
			}
			CreatePerksModifiersInfo(runtimePerk);
			if (perkVisual != null)
			{
				perkVisual.SetPerkVisuals(runtimePerk.RuntimeData);
			}
			if ((bool)titleText)
			{
				titleText.text = runtimePerk.RuntimeData.Data.GetTitle();
			}
			if ((bool)levelNumberTxt)
			{
				levelNumberTxt.text = runtimePerk.Level.ToString();
			}
		}

		private void CreatePerksModifiersInfo(RuntimePerk runtimePerk)
		{
			if ((!runStatsPerkEffectsInfoPrefab || !runStatsPerkEffectsInfoContainer) && runtimePerk.RuntimeData.Data.RaritiesCount != 0)
			{
				PerkRarityModifiersData rarity = runtimePerk.RuntimeData.Data.GetRarity(runtimePerk.CurrentRarity);
				for (int i = 0; i < rarity.Modifiers.Length; i++)
				{
					Object.Instantiate(runStatsPerkEffectsInfoPrefab, runStatsPerkEffectsInfoContainer.transform).SetPerkEffectInfo(runtimePerk, i);
				}
			}
		}

		private void SetInactive()
		{
			titleText.text = "- - - - -";
			levelNumberTxt.gameObject.SetActive(value: false);
			levelSphere.SetActive(value: false);
			levelTxt.SetActive(value: false);
			RuntimePerkData perkVisuals = null;
			perkVisual.SetPerkVisuals(perkVisuals);
		}
	}
}
