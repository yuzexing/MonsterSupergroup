using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data.Perks;
using AstralShift.UI;
using TMPro;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Menus
{
	public class PerkStatMenuButton : CustomUIButton
	{
		[SerializeField]
		private UICardOrPerkStaticElement perkVisuals;

		[SerializeField]
		private TextMeshProUGUI perkUpgradeCount;

		public void SetPerk(RuntimePerk runtimePerk)
		{
			perkVisuals.SetPerkVisuals(new RuntimePerkData(runtimePerk.RuntimeData.Data, runtimePerk.CurrentRarity));
			perkUpgradeCount.SetText(runtimePerk.Level.ToString());
		}
	}
}
