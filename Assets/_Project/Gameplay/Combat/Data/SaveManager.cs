using System.Linq;
using System.Threading.Tasks;
using Assets.Scripts.SaveSystem.Porting_Helpers;
using AstralShift.Helpers;
using Newtonsoft.Json;
using PixelCrushers;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	public class SaveManager
	{
		private static SaveHelper saveSystem;

		public static async Task<string> SaveGame(int saveslot)
		{
			GameDataManager.Instance.ShowSavingIndicator();
			GameData.Instance.SaveVersion = Constants.SaveVersion;
			string result = await saveSystem.SaveGame(saveslot);
			GameDataManager.Instance.HideSavingIndicator().Forget();
			return result;
		}

		public static Task LoadGameFromSaveSlotAsync(int saveslot)
		{
			return LoadGameFromFileAsync(GetSaveSlotFilePath(saveslot));
		}

		public static async Task LoadGameFromFileAsync(string filename)
		{
			string filepath = saveSystem.GetSaveFolderPath() + filename;
			Debug.Log("starting task");
			Task<string> task = ReadSaveFromFile(filepath);
			task.Start();
			await task;
			Debug.Log("task ended");
			GameData.Instance = JsonConvert.DeserializeObject<GameData>(task.Result, new JsonSerializerSettings
			{
				ContractResolver = new NewtonsoftPrivateResolver(),
				ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor
			});
			int saveVersion = GameData.Instance.SaveVersion;
			if (Constants.SaveVersion != saveVersion)
			{
				Debug.Log("Different Save File Version found: " + saveVersion);
				_ = 1;
			}
			// SaveSystem.ApplySavedGameData(GameData.Instance.DialogueSystemSavedGameData);
			GameDataManager.Instance.RefreshState();
		}

		private static Task<string> ReadSaveFromFile(string filepath)
		{
			return saveSystem.ReadSaveFromFile(filepath);
		}

		private static string GetSaveSlotFilePath(int saveslot)
		{
			return saveSystem.GetSaveSlotFilePath(saveslot);
		}

		public static string[] GetSaveFileNames()
		{
			if (saveSystem == null)
			{
				return null;
			}
			return saveSystem.GetSaveFileNames()?.Where((string save) => save != null).ToArray();
		}

		public static bool HasSaveFiles()
		{
			if (saveSystem == null)
			{
				return false;
			}
			string[] saveFileNames = saveSystem.GetSaveFileNames();
			if (saveFileNames == null)
			{
				return false;
			}
			return saveFileNames.Length != 0;
		}

		public static void InitializeSaveData()
		{
			saveSystem = new WindowsSaveSystem();
			saveSystem.InitializeSaveData();
		}

		public static void CleanupSaveData()
		{
			saveSystem.Cleanup();
		}
	}
}
