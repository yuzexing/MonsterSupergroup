using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace AstralShift.DebugTools
{
	public static class DBL
	{
		public enum Module
		{
			Controllers = 0,
			ControllerStack = 1,
			UIMenuWindow = 2,
			Timeline = 3,
			DataMod = 4,
			Dialogue = 5,
			FSM = 6,
			ToastManager = 7,
			PlayerHand = 8,
			Settings = 9,
			AttackToken = 10,
			PlayerAttacks = 11,
			EnemyAttacks = 12,
			ProgressionTimeline = 13,
			Items = 14,
			CardPool = 15
		}

		private const string Controllers = "<color=yellow><b>► Controllers: </b></color>";

		private const string ControllerStack = "<color=orange><b>► ControllerStack: </b></color>";

		private const string UIMenuWindow = "<color=cyan><b>► UIMenuWindows: </b></color>";

		private const string Timeline = "<color=green><b>► Timeline: </b></color>";

		private const string DataMod = "<color=red><b>► DataMod: </b></color>";

		private const string Dialogue = "<color=teal><b>► Dialogue: </b></color>";

		private const string FSM = "<color=blue><b>► FSM: </b></color>";

		private const string ToastManager = "<color=violet><b>► ToastManager: </b></color>";

		private const string PlayerHand = "<color=cyan><b>► PlayerHand: </b></color>";

		private const string Settings = "<color=cyan><b>► Settings: </b></color>";

		private const string AttackToken = "<color=magenta><b>► Attack Token: </b></color>";

		private const string PlayerAttacks = "<color=magenta><b>► Player Attacks: </b></color>";

		private const string EnemyAttacks = "<color=red><b>► Enemy Attacks: </b></color>";

		private const string Items = "<color=lime><b>► Items: </b></color>";

		private const string ProgressionTimeline = "<color=red><b>► Progression Timeline: </b></color>";

		private const string CardPool = "<color=red><b>► CardPool: </b></color>";

		public static void Log(Module module, string msg, int severity = 0)
		{
			string text = module switch
			{
				Module.Controllers => "<color=yellow><b>► Controllers: </b></color>", 
				Module.ControllerStack => "<color=orange><b>► ControllerStack: </b></color>", 
				Module.UIMenuWindow => "<color=cyan><b>► UIMenuWindows: </b></color>", 
				Module.Timeline => "<color=green><b>► Timeline: </b></color>", 
				Module.DataMod => "<color=red><b>► DataMod: </b></color>", 
				Module.Dialogue => "<color=teal><b>► Dialogue: </b></color>", 
				Module.FSM => "<color=blue><b>► FSM: </b></color>", 
				Module.ToastManager => "<color=violet><b>► ToastManager: </b></color>", 
				Module.PlayerHand => "<color=cyan><b>► PlayerHand: </b></color>", 
				Module.Settings => "<color=cyan><b>► Settings: </b></color>", 
				Module.AttackToken => "<color=magenta><b>► Attack Token: </b></color>", 
				Module.PlayerAttacks => "<color=magenta><b>► Player Attacks: </b></color>", 
				Module.EnemyAttacks => "<color=red><b>► Enemy Attacks: </b></color>", 
				Module.ProgressionTimeline => "<color=red><b>► Progression Timeline: </b></color>", 
				Module.Items => "<color=lime><b>► Items: </b></color>", 
				Module.CardPool => "<color=red><b>► CardPool: </b></color>", 
				_ => "► NO MODULE: ", 
			};
			switch (severity)
			{
			case 0:
				UnityEngine.Debug.Log(text + msg);
				break;
			case 1:
				UnityEngine.Debug.LogWarning(text + msg);
				break;
			case 2:
				UnityEngine.Debug.LogError(text + msg);
				break;
			}
		}

		public static void LogHardwareInfo()
		{
			string processorType = SystemInfo.processorType;
			string graphicsDeviceName = SystemInfo.graphicsDeviceName;
			int systemMemorySize = SystemInfo.systemMemorySize;
			string diskModel = GetDiskModel();
			string monitorModel = GetMonitorModel();
			UnityEngine.Debug.Log($"--- Hardware Info ---\nCPU Model: {processorType}\nGPU Model: {graphicsDeviceName}\nRAM: {systemMemorySize} MB\nInstall Disk Model: {diskModel}\nMain Monitor Model: {monitorModel}\n---------------------");
		}

		private static string GetDiskModel()
		{
			try
			{
				string text = Path.GetPathRoot(Application.dataPath).Substring(0, 1);
				using Process process = Process.Start(new ProcessStartInfo
				{
					FileName = "powershell.exe",
					Arguments = "-NoProfile -Command \"(Get-Disk (Get-Partition -DriveLetter '" + text + "').DiskNumber).Model\"",
					UseShellExecute = false,
					RedirectStandardOutput = true,
					CreateNoWindow = true
				});
				if (process != null)
				{
					string text2 = process.StandardOutput.ReadToEnd();
					process.WaitForExit();
					if (!string.IsNullOrWhiteSpace(text2))
					{
						return text2.Trim();
					}
				}
			}
			catch (Exception)
			{
			}
			return "Unknown Disk Model";
		}

		private static string GetMonitorModel()
		{
			string name = Screen.mainWindowDisplayInfo.name;
			if (string.IsNullOrEmpty(name))
			{
				return "Unknown Monitor";
			}
			return name;
		}
	}
}
