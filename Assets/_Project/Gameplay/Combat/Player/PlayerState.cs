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
		public static bool IsBusy()
		{
			return !(ControllerManager.Instance.CurrentController is PlayerController_HMD playerController_HMD) || playerController_HMD.InBusyState;
		}

		public static bool IsLevelingUp()
		{
			if (ControllerManager.Instance.CurrentController is PlayerController_HMD playerController_HMD)
			{
				return playerController_HMD.InLevelingUpState;
			}
			return false;
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
			if (!(ControllerManager.Instance.CurrentController is HoraceUltimateController))
			{
				return ControllerManager.Instance.CurrentController is NoMovementPlayerController;
			}
			return true;
		}
	}
}
