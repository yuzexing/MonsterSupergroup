using System;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.HellMaiden.UI;
using AstralShift.Initialization;
using AstralShift.Managers;
using Com.LuisPedroFonseca.ProCamera2D;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Scenes.SceneLoaders
{
	public class PlayerLoader : SceneLoader
	{
		public Transform spawnPosition;

		public bool restartPlayerLevel = true;

		public bool restartLeveler = true;

		public bool enableCombatGamepadPointer = true;

		public bool equipSignature;

		private const string ControlsTutorialTrigger = "Number_Attempts";

		private const int ControlsTutorialNumberOfAttempts = 3;

		public override async UniTask LoadAsync()
		{
			ProCamera2D.Instance?.RemoveAllCameraTargets();
			ProCamera2D.Instance?.AddCameraTarget(GameDirector.Instance.Player.transform);
			PauseManager.Instance.ResetTimeScale();
			if ((bool)spawnPosition)
			{
				GameDirector.Instance.Player.transform.position = spawnPosition.position;
			}
			ProCamera2D.Instance?.CenterOnTargets();
			if (restartPlayerLevel)
			{
				if (restartLeveler)
				{
					Leveler.Instance.Init();
				}
				GameDirector.Instance.Player.RestartStats();
				GameDirector.Instance.Player.RestartPlayer();
				PlayerHand.Instance.ClearAll();
				if (equipSignature)
				{
					PlayerHand.Instance.TryEquipSignatureWeapon();
				}
			}
			else
			{
				GameDirector.Instance.Player.RestartPlayer();
			}
			GameDirector.Instance.Player.SubscribeGameEvents();
			SceneMaster.Instance.OnSceneShowStart += ActivatePlayer;
			GameEvents instance = GameEvents.Instance;
			instance.OnAreaNamePopupClosed = (Action)Delegate.Combine(instance.OnAreaNamePopupClosed, new Action(TryLaunchControlsTutorial));
		}

		private void OnDestroy()
		{
			GameEvents instance = GameEvents.Instance;
			instance.OnAreaNamePopupClosed = (Action)Delegate.Remove(instance.OnAreaNamePopupClosed, new Action(TryLaunchControlsTutorial));
		}

		private void ActivatePlayer()
		{
			PlayerHand.Instance.ActivateWeapons();
			PointerManager.Instance.EnableBattlePointer(enableCombatGamepadPointer);
			ControllerManager.Instance.OverrideGameController<PlayerController_HMD>();
		}

		private void TryLaunchControlsTutorial()
		{
			GameEvents instance = GameEvents.Instance;
			instance.OnAreaNamePopupClosed = (Action)Delegate.Remove(instance.OnAreaNamePopupClosed, new Action(TryLaunchControlsTutorial));
			int gameInt = GameDataManager.GetGameInt("Number_Attempts");
			if (gameInt <= 3 && gameInt > 1)
			{
				TutorialManager.Instance.Controls.TryLaunchControlsTutorial(null);
			}
		}
	}
}
