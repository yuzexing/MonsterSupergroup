using System;
using System.Threading.Tasks;

namespace Assets.Scripts.SaveSystem.Porting_Helpers
{
	public abstract class SaveHelper
	{
		public abstract Task<string> SaveGame(int saveslot);

		public abstract string ReadSave(string filePath);

		public abstract string[] GetSaveFileNames();

		public abstract string GetSaveSlotFilePath(int saveslot);

		public abstract string GetSaveFolderPath();

		public abstract void InitializeSaveData();

		public abstract void Cleanup();

		public string GenerateSaveFileName(int saveslot)
		{
			long ticks = DateTime.Now.Ticks;
			return string.Concat(saveslot + "_" + ticks, ".json");
		}

		public virtual Task<string> ReadSaveFromFile(string filePath)
		{
			return new Task<string>(() => ReadSave(filePath));
		}
	}
}
