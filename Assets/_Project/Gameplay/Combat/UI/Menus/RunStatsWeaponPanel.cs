using System.Collections.Generic;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.GameStats;
using TMPro;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Menus
{
	public class RunStatsWeaponPanel : MonoBehaviour
	{
		[SerializeField]
		private CanvasGroup _canvasGroup;

		[SerializeField]
		private UICardOrPerkStaticElement weaponCardVisuals;

		[SerializeField]
		private List<UICardOrPerkStaticElement> equipmentVisuals;

		[SerializeField]
		private TextMeshProUGUI weaponName;

		[SerializeField]
		private TextMeshProUGUI totalDamage;

		[SerializeField]
		private TextMeshProUGUI totalHits;

		[SerializeField]
		private TextMeshProUGUI enemyDeaths;

		[SerializeField]
		private List<TextMeshProUGUI> equipmentNames;

		private WeaponStatsEntry _weaponStatsEntry;

		public CanvasGroup CanvasGroup => _canvasGroup;

		public void Initialize(WeaponStatsEntry entry, RuntimeWeaponData runtimeWeapon, List<RuntimeEquipmentData> runtimeEquipments)
		{
			if (weaponCardVisuals != null)
			{
				weaponCardVisuals.SetCardVisuals(runtimeWeapon);
			}
			for (int i = 0; i < equipmentVisuals.Count; i++)
			{
				if (i < runtimeEquipments.Count)
				{
					equipmentVisuals[i].SetCardVisuals(runtimeEquipments[i]);
					equipmentNames[i].text = runtimeEquipments[i].Data.GetTitle();
				}
				else
				{
					equipmentNames[i].text = "- - - - - -";
				}
			}
			_weaponStatsEntry = entry;
			RefreshValues(runtimeWeapon.Data);
		}

		private void RefreshValues(WeaponData weaponData)
		{
			if ((bool)weaponName)
			{
				weaponName.text = weaponData.GetTitle();
			}
			if ((bool)totalDamage)
			{
				totalDamage.text = _weaponStatsEntry.TotalDamage.ToString();
			}
			if ((bool)totalHits)
			{
				totalHits.text = _weaponStatsEntry.TotalHits.ToString();
			}
			if ((bool)enemyDeaths)
			{
				enemyDeaths.text = _weaponStatsEntry.EnemyDeaths.ToString();
			}
		}
	}
}
