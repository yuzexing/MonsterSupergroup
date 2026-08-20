using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Menus.MetaProgression
{
	public class MetaCostView : MonoBehaviour
	{
		public Image background;

		public Sprite bgNormal;

		public Sprite bgMaxed;

		public GameObject icon;

		public TMP_Text text;

		private static string maxCostLocalizeID = "META_MAXCOST";

		public void SetCost(int cost)
		{
			background.sprite = bgNormal;
			text.text = cost.ToString();
			icon.SetActive(value: true);
		}

		public void SetMaxedOut()
		{
			background.sprite = bgMaxed;
			string term = maxCostLocalizeID;
			LocalizationMediator.GetTranslation(ref term);
			if (term != null)
			{
				text.text = term;
			}
			else
			{
				text.text = "MAX";
			}
			icon.SetActive(value: false);
		}
	}
}
