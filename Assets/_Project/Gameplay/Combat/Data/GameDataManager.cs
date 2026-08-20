using System;
using System.Threading;
using System.Threading.Tasks;
using Assets.Scripts.AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.GameStats;
using Cysharp.Threading.Tasks;
using PixelCrushers;
// using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	public class GameDataManager : MonoBehaviour
	{
		public static GameDataManager Instance;

		public Animator saveIndicator;

		public Action OnRefresh;

		private CancellationTokenSource _savingIndicatorCTS;

		private const float SavingIndicatorTimeout = 1f;

		public float lastSaveTime { get; private set; }

		public void Init()
		{
			if (Instance != null && Instance != this)
			{
				UnityEngine.Object.Destroy(base.gameObject);
				return;
			}
			Instance = this;
			SaveManager.InitializeSaveData();
			SaveManager.HasSaveFiles();
		}

		public uint GetSignatureWeaponID()
		{
			return GameData.Instance.signatureWeaponID;
		}

		public void RegisterSignatureWeaponID(uint id)
		{
			GameData.Instance.signatureWeaponID = id;
		}

		public static void RegisterCutscene(string cutsceneID)
		{
			if (!GameData.Instance.viewedCutscenes.Contains(cutsceneID))
			{
				GameData.Instance.viewedCutscenes.Add(cutsceneID);
			}
			Debug.Log("Registered Cutscene " + cutsceneID);
		}

		public static void RegisterDialogue(string dialogueID)
		{
			if (!GameData.Instance.viewedDialogues.Contains(dialogueID))
			{
				GameData.Instance.viewedDialogues.Add(dialogueID);
			}
			Debug.Log("Registered Dialogue " + dialogueID);
		}

		public static bool HasCutscenePlayed(string cutsceneID)
		{
			return GameData.Instance.viewedCutscenes.Contains(cutsceneID);
		}

		public static bool HasDialoguePlayed(string dialogueID)
		{
			return GameData.Instance.viewedDialogues.Contains(dialogueID);
		}

		public static void UnlockPoet(PoetPoolID poetPoolID)
		{
			if (!GameData.Instance.unlockedPoets.Contains(poetPoolID))
			{
				GameData.Instance.unlockedPoets.Add(poetPoolID);
			}
		}

		public static bool IsPoetUnlocked(PoetPoolID poetPoolID)
		{
			return GameData.Instance.unlockedPoets.Contains(poetPoolID);
		}

		public static void RegisterGameTrigger(string triggerID, bool state)
		{
			// DialogueLua.SetVariable(triggerID, state);
		}

		public static bool GetGameTriggerState(string triggerID)
		{
			// return DialogueLua.GetVariable(triggerID).asBool;
			return false;
		}

		public static void RegisterGameInt(string numberID, int number)
		{
			// DialogueLua.SetVariable(numberID, number);
		}

		public static void IncrementGameInt(string numberID, int number = 1)
		{
			RegisterGameInt(numberID, GetGameInt(numberID) + number);
		}

		public static int GetGameInt(string numberID)
		{
			// return DialogueLua.GetVariable(numberID).asInt;
			return 0;
		}

		public Task<string> SaveGameData()
		{
			// SavedGameData dialogueSystemSavedGameData = SaveSystem.RecordSavedGameData();
			// GameData.Instance.DialogueSystemSavedGameData = dialogueSystemSavedGameData;
			lastSaveTime = Time.time;
			return SaveManager.SaveGame(0);
		}

		public void ResetData()
		{
			// SaveSystem.ClearSavedGameData();
			// DialogueManager.ResetDatabase(DatabaseResetOptions.KeepAllLoaded);
			GameData.Instance.Reset();
			PlayerHand.Instance.Reset();
			RefreshState();
		}

		internal void RefreshState()
		{
			OnRefresh?.Invoke();
		}

		public void ShowSavingIndicator()
		{
			if (_savingIndicatorCTS != null)
			{
				_savingIndicatorCTS.Cancel();
				_savingIndicatorCTS.Dispose();
				_savingIndicatorCTS = null;
			}
			if (_savingIndicatorCTS == null)
			{
				_savingIndicatorCTS = new CancellationTokenSource();
			}
			saveIndicator.SetBool("Show", value: true);
		}

		public async UniTaskVoid HideSavingIndicator()
		{
			if (_savingIndicatorCTS == null)
			{
				return;
			}
			try
			{
				using CancellationTokenSource linkedCTS = CancellationTokenSource.CreateLinkedTokenSource(_savingIndicatorCTS.Token, base.destroyCancellationToken);
				await UniTask.Delay(TimeSpan.FromSeconds(1.0), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, linkedCTS.Token, cancelImmediately: true);
				saveIndicator.SetBool("Show", value: false);
			}
			catch (OperationCanceledException)
			{
			}
		}

		public static int GetMetaProgressionLevel(MetaProgressionID metaProgressionID)
		{
			if (GameData.Instance.MetaProgressionSaveData.unlockedLevels.ContainsKey(metaProgressionID))
			{
				return GameData.Instance.MetaProgressionSaveData.unlockedLevels[metaProgressionID];
			}
			return 0;
		}

		public static void SetMetaProgressionLevel(MetaProgressionID metaProgressionID, int lvl)
		{
			if (GameData.Instance.MetaProgressionSaveData.unlockedLevels.ContainsKey(metaProgressionID))
			{
				GameData.Instance.MetaProgressionSaveData.unlockedLevels[metaProgressionID] = lvl;
			}
			else
			{
				GameData.Instance.MetaProgressionSaveData.unlockedLevels.Add(metaProgressionID, lvl);
			}
		}

		public static void JoinRunStats(RunStatsTracker runStats)
		{
			GameData.Instance.GameStatsTracker.SaveEndOfRunStats(runStats);
		}

		public static void IncreaseCurrency(int value)
		{
			GameData.Instance.currency = Mathf.Clamp(GameData.Instance.currency + value, 0, 9999);
			GameEvents.Instance.OnCurrencyChanged?.Invoke(GameData.Instance.currency);
		}

		public static void DecreaseCurrency(int value)
		{
			GameData.Instance.currency = Mathf.Clamp(GameData.Instance.currency - value, 0, 9999);
			GameEvents.Instance.OnCurrencyChanged?.Invoke(GameData.Instance.currency);
		}

		public static int GetCurrency()
		{
			return GameData.Instance.currency;
		}
	}
}
