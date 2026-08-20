using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.UI;
using AstralShift.HellMaiden.UI.Menus;
using AstralShift.Managers;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class OpenMenuInteraction : Interaction
	{
		private enum MenuType
		{
			WeaponSelectionMenu = 0,
			CardPickMenu = 1,
			StatsMenu = 2,
			MetaProgression = 3,
			AchievementMenu = 4
		}

		[SerializeField]
		private MenuType menu;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			OpenMenu();
			OnEnd();
		}

		private void OpenMenu()
		{
			switch (menu)
			{
			case MenuType.WeaponSelectionMenu:
				WeaponSelectionMenuView.Instance?.Open();
				break;
			case MenuType.CardPickMenu:
				UICardPickMenuView.Instance?.Open();
				break;
			case MenuType.StatsMenu:
				CombatUIManager.Instance?.OpenStatsMenu();
				break;
			case MenuType.MetaProgression:
				ControllerManager.Instance.OverrideGameController<MetaProgressionMenuController>().Open();
				break;
			case MenuType.AchievementMenu:
				ControllerManager.Instance.OverrideGameController<AchievementMenuController>().Open();
				break;
			}
		}
	}
}
