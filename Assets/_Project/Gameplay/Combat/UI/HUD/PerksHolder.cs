using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data.Perks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.HUD
{
	public class PerksHolder : MonoBehaviour
	{
		[SerializeField]
		private RectTransform layoutGroupRectTransform;

		[SerializeField]
		private List<RectTransform> perksTransforms;

		[SerializeField]
		private List<TextMeshProUGUI> perkCountTexts;

		private int[] _perksCount = new int[Enum.GetValues(typeof(PerkRarity)).Length];

		private void Awake()
		{
			PlayerHand.Instance.OnPerkAdded += Refresh;
			Refresh();
		}

		private void OnDestroy()
		{
			PlayerHand.Instance.OnPerkAdded -= Refresh;
		}

		public void Refresh(RuntimePerk perk = null)
		{
			UpdateCount();
			for (int i = 0; i < perkCountTexts.Count; i++)
			{
				SetCountText(i, _perksCount[i]);
			}
			LayoutRebuilder.ForceRebuildLayoutImmediate(layoutGroupRectTransform);
		}

		private void SetCountText(int index, int count)
		{
			if (count > 0)
			{
				perksTransforms[index].gameObject.SetActive(value: true);
				perkCountTexts[index].SetText($"{count}");
			}
			else
			{
				perksTransforms[index].gameObject.SetActive(value: false);
			}
		}

		private void UpdateCount()
		{
			ResetCount();
			List<RuntimePerk> perksList = PlayerHand.Instance.PerksList;
			for (int i = 0; i < perksList.Count; i++)
			{
				_perksCount[(int)perksList[i].CurrentRarity]++;
			}
		}

		private void ResetCount()
		{
			for (int i = 0; i < _perksCount.Length; i++)
			{
				_perksCount[i] = 0;
			}
		}
	}
}
