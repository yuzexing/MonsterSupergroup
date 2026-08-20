using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AstralShift.HellMaiden.Data;
using Newtonsoft.Json;
using UnityEngine;

namespace Assets.Scripts.SaveSystem.Porting_Helpers
{
	public class WindowsSaveSystem : SaveHelper
	{
		private static readonly Guid FOLDERID_SavedGames = new Guid("4C5C32FF-BB9D-43B0-B5B4-2D72E54EAAA4");

		private const string SaveFolderName = "/HellMaiden/";

		[DllImport("shell32.dll")]
		private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

		[DllImport("ole32.dll")]
		private static extern void CoTaskMemFree(IntPtr ptr);

		public static string GetSavedGamesFolderPath()
		{
			if (SHGetKnownFolderPath(FOLDERID_SavedGames, 0u, IntPtr.Zero, out var ppszPath) >= 0)
			{
				string result = Marshal.PtrToStringUni(ppszPath);
				CoTaskMemFree(ppszPath);
				return result;
			}
			return null;
		}

		public override Task<string> SaveGame(int saveslot)
		{
			return Task.Run(delegate
			{
				JsonSerializer jsonSerializer = new JsonSerializer
				{
					NullValueHandling = NullValueHandling.Ignore
				};
				string saveSlotFilePath = GetSaveSlotFilePath(saveslot);
				if (saveSlotFilePath != null)
				{
					saveSlotFilePath = GetSaveFolderPath() + saveSlotFilePath;
					File.Delete(saveSlotFilePath);
				}
				string text = GenerateSaveFileName(saveslot);
				string path = GetSaveFolderPath() + text;
				new FileInfo(text).Directory.Create();
				using (StreamWriter textWriter = new StreamWriter(path))
				{
					using JsonWriter jsonWriter = new JsonTextWriter(textWriter);
					jsonSerializer.Serialize(jsonWriter, GameData.Instance);
				}
				Debug.Log("Game Saved in slot " + saveslot);
				return text;
			});
		}

		public override string ReadSave(string filePath)
		{
			new JsonSerializer().NullValueHandling = NullValueHandling.Ignore;
			return File.ReadAllText(filePath);
		}

		public override string GetSaveSlotFilePath(int saveslot)
		{
			FileInfo[] files = new FileInfo(GetSaveFolderPath()).Directory.GetFiles();
			for (int i = 0; i < files.Length; i++)
			{
				if (files[i].Name.EndsWith(".json") && files[i].Name.Substring(0, files[i].Name.IndexOf("_")) == saveslot.ToString())
				{
					return files[i].Name;
				}
			}
			return null;
		}

		public override string[] GetSaveFileNames()
		{
			FileInfo fileInfo = new FileInfo(GetSaveFolderPath());
			if (fileInfo.Directory.Exists)
			{
				return (from e in fileInfo.Directory.GetFiles()
					where e.Extension == ".json"
					select e.Name).ToArray();
			}
			fileInfo.Directory.Create();
			return new string[0];
		}

		public override string GetSaveFolderPath()
		{
			return GetSavedGamesFolderPath().Replace("\\", "/") + "/HellMaiden/";
		}

		public override void InitializeSaveData()
		{
		}

		public override void Cleanup()
		{
			string saveSlotFilePath = GetSaveSlotFilePath(0);
			if (saveSlotFilePath != null)
			{
				saveSlotFilePath = GetSaveFolderPath() + saveSlotFilePath;
				File.Delete(saveSlotFilePath);
			}
		}
	}
}
