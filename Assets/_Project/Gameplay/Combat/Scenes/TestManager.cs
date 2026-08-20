using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Data;
using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEngine;

namespace AstralShift.HellMaiden.Scenes
{
	public class TestManager : MonoBehaviour
	{
		public GameObject DebugButtons;

		public string _Language;

		public TMP_Dropdown poetDropdown;

		private List<string> poetPoolList;

		public TMP_Dropdown triggerDropdown;

		private List<string> triggerList;

		public List<CutscenePreset> cutscenePresets;

		public Transform cutsceneContainer;

		public GameObject cutsceneEntryPrefab;

		public TMP_Dropdown achievementDropdown;

		private List<string> achievementList;

		private PoetPoolID _PoetPoolID;

		private string _trigger;

		private string _achievement;

		public void Init()
		{
			poetPoolList = Enum.GetNames(typeof(PoetPoolID)).ToList();
			poetDropdown.options.Clear();
			poetDropdown.AddOptions(poetPoolList);
			poetDropdown.onValueChanged.AddListener(SelectPoetOption);
			// triggerList = (from e in DialogueManager.instance.MasterDatabase.variables.FindAll((Variable e) => e.Type == FieldType.Boolean)
			// 	select e.Name).ToList();
			triggerDropdown.options.Clear();
			// triggerDropdown.AddOptions(triggerList);
			triggerDropdown.onValueChanged.AddListener(SelectTriggerOption);
			achievementList = Enum.GetNames(typeof(AchievementManager.AchievementID)).ToList();
			achievementDropdown.options.Clear();
			achievementDropdown.AddOptions(achievementList);
			achievementDropdown.onValueChanged.AddListener(SelectAchievement);
			SetupCutsceneList();
		}

		public void ReloadScene()
		{
			SceneMaster.Instance.LoadScene(SceneMaster.Instance.CurrentSceneEnum);
		}

		public void SaveGame(int slot = 0)
		{
			GameDataManager.Instance.SaveGameData();
		}

		public void SelectPoetOption(int option)
		{
			_PoetPoolID = Enum.Parse<PoetPoolID>(poetPoolList[option]);
		}

		public void UnlockPoet()
		{
			GameDirector.Instance.runtimeDB.UnlockPoetPool(_PoetPoolID);
		}

		public void SelectTriggerOption(int option)
		{
			_trigger = triggerList[option];
		}

		public void SetDialogueTrigger()
		{
			GameDataManager.RegisterGameTrigger(_trigger, state: true);
		}

		public void RemoveDialogueTrigger()
		{
			GameDataManager.RegisterGameTrigger(_trigger, state: false);
		}

		public void SelectAchievement(int option)
		{
			_achievement = achievementList[option];
		}

		public void UnlockAchievement()
		{
			Enum.TryParse<AchievementManager.AchievementID>(_achievement, out var result);
			AchievementManager.Instance.UnlockAchievement(result);
		}

		public void IncreaseWails(int amount)
		{
			GameDataManager.IncreaseCurrency(amount);
		}

		private void SetupCutsceneList()
		{
			foreach (CutscenePreset cutscenePreset in cutscenePresets)
			{
				UnityEngine.Object.Instantiate(cutsceneEntryPrefab, cutsceneContainer).GetComponent<CutsceneDebugEntry>().Setup(cutscenePreset);
			}
		}
	}
}
