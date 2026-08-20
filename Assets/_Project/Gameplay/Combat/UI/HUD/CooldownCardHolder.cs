using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.HUD
{
	public class CooldownCardHolder : MonoBehaviour
	{
		[SerializeField]
		private Canvas canvas;

		[SerializeField]
		private Color greyColor = Color.grey;

		[SerializeField]
		private List<UICardOrPerkStaticElement> bgCooldownCards;

		[SerializeField]
		private List<UICardOrPerkStaticElement> frontCooldownCards;

		[SerializeField]
		private List<CooldownCard> cooldownCards;

		public void Initialize()
		{
			PlayerHand.Instance.OnSlotWeaponChanges += SetSlotWeapon;
			for (int i = 0; i < bgCooldownCards.Count; i++)
			{
				bgCooldownCards[i].Hide();
				frontCooldownCards[i].Hide();
			}
		}

		private void OnDestroy()
		{
			PlayerHand.Instance.OnSlotWeaponChanges -= SetSlotWeapon;
		}

		private async void SetSlotWeapon(int slot, RuntimeWeaponData weaponData, WeaponBehaviour weaponBehaviour)
		{
			_ = 1;
			try
			{
				if (!weaponBehaviour)
				{
					bgCooldownCards[slot].Hide();
					frontCooldownCards[slot].Hide();
					cooldownCards[slot].SetWeapon(null);
					return;
				}
				bgCooldownCards[slot].SetColor(greyColor);
				await bgCooldownCards[slot].SetCardVisualsAsync(weaponData);
				await frontCooldownCards[slot].SetCardVisualsAsync(weaponData);
				cooldownCards[slot].SetWeapon(weaponBehaviour);
				bgCooldownCards[slot].Show();
				frontCooldownCards[slot].Show();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		public async void Refresh()
		{
			_ = 1;
			try
			{
				for (int i = 0; i < bgCooldownCards.Count; i++)
				{
					await bgCooldownCards[i].RefreshCardVisualsAsync();
					await frontCooldownCards[i].RefreshCardVisualsAsync();
				}
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}
	}
}
