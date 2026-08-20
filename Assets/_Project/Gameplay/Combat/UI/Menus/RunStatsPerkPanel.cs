using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand;
using UnityEngine;
using UnityEngine.Serialization;

namespace AstralShift.HellMaiden.UI.Menus
{
	public class RunStatsPerkPanel : MonoBehaviour
	{
		[SerializeField]
		private CanvasGroup canvasGroup;

		[FormerlySerializedAs("perksVisuals")]
		[SerializeField]
		private List<EndStatsPerk> perks = new List<EndStatsPerk>();

		public CanvasGroup CanvasGroup => canvasGroup;

		public void SetPerksVisuals(List<RuntimePerk> chosenPerks)
		{
			for (int i = 0; i < perks.Count; i++)
			{
				if (chosenPerks.Count > i)
				{
					perks[i].SetPerkInfo(chosenPerks[i]);
				}
				else
				{
					perks[i].SetPerkInfo(null);
				}
			}
		}
	}
}
