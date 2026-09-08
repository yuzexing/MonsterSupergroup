using System.Linq;
using Assets.Scripts.AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Quests;
using AstralShift.Managers;
using PixelCrushers.DialogueSystem;

namespace AstralShift.HellMaiden.Player
{
	public static class PlayerState
	{
		public static bool IsBusy(PlayerMovement player) => player == null ||
			!player.IsRuntimeInitialized || player.IsUpgradeSelectionLocked ||
			(player.CombatantBinding != null && !player.CombatantBinding.IsAlive);

		public static bool IsLevelingUp(PlayerMovement player) =>
			player != null && player.IsUpgradeSelectionLocked;

		// Compatibility for unmigrated local-only menus/items. Gameplay callers
		// must supply their player explicitly and must never query the input stack.
		public static bool IsBusy()
		{
			return IsBusy(GameDirector.Instance != null ? GameDirector.Instance.Player : null);
		}

		public static bool IsLevelingUp()
		{
			return IsLevelingUp(GameDirector.Instance != null ? GameDirector.Instance.Player : null);
		}

		public static bool IsInQuest()
		{
			if (!ProgressionManager.Instance)
			{
				return false;
			}
			DivinaQuestGoal[] array = ProgressionManager.Instance.Quests?.Where((DivinaQuestGoal element) => element.questState == QuestState.Active && element.IsMainQuest).ToArray();
			if (array != null)
			{
				return array.Length != 0;
			}
			return false;
		}

		public static bool IsInControllerBasedUltimateAttackController()
		{
			if (ControllerManager.Instance == null || ControllerManager.Instance.Stack == null) return false;
			if (!(ControllerManager.Instance.CurrentController is HoraceUltimateController))
			{
				return ControllerManager.Instance.CurrentController is NoMovementPlayerController;
			}
			return true;
		}
	}
}
