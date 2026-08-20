using System;
using System.Collections;
using System.Text;
using AOT;
// using Steamworks;
using UnityEngine;

namespace AstralShift.Helpers.Steam
{
	[DisallowMultipleComponent]
	public class SteamManager : MonoBehaviour
	{
	// 	protected static bool s_EverInitialized;
	//
	// 	private static CallResult<UserStatsReceived_t> OnUserStatsReceivedCallResult;
	//
	// 	protected Callback<UserStatsReceived_t> m_UserStatsReceived;
	//
		protected static SteamManager s_instance;
	//
		protected bool _bInitialized;
	//
	// 	private Coroutine _updateCallbacksCoroutine;
	//
	// 	protected SteamAPIWarningMessageHook_t m_SteamAPIWarningMessageHook;
	//
	// 	protected static Callback<GameOverlayActivated_t> _GameOverlayActivated;
	//
	// 	private static bool _OverlayOpened;
	//
		protected static SteamManager Instance
		{
			get
			{
				if (s_instance == null)
				{
					return new GameObject("SteamManager").AddComponent<SteamManager>();
				}
				return s_instance;
			}
		}
	
		public static bool Initialized
		{
			get
			{
				if (!Instance._bInitialized)
				{
					Debug.LogWarning("[Steamworks.NET] Steam Manager not Initialized");
				}
				return Instance._bInitialized;
			}
		}
	//
	// 	public static bool OverlayOpened => _OverlayOpened;
	//
	// 	[MonoPInvokeCallback(typeof(SteamAPIWarningMessageHook_t))]
	// 	protected static void SteamAPIDebugTextHook(int nSeverity, StringBuilder pchDebugText)
	// 	{
	// 		Debug.LogWarning(pchDebugText);
	// 	}
	//
	// 	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	// 	private static void InitOnPlayMode()
	// 	{
	// 		s_EverInitialized = false;
	// 		s_instance = null;
	// 	}
	//
	// 	protected virtual void Awake()
	// 	{
	// 		if (s_instance != null)
	// 		{
	// 			UnityEngine.Object.Destroy(base.gameObject);
	// 			return;
	// 		}
	// 		s_instance = this;
	// 		if (s_EverInitialized)
	// 		{
	// 			throw new Exception("Tried to Initialize the SteamAPI twice in one session!");
	// 		}
	// 		UnityEngine.Object.DontDestroyOnLoad(base.gameObject);
	// 		if (!Packsize.Test())
	// 		{
	// 			Debug.LogError("[Steamworks.NET] Packsize Test returned false, the wrong version of Steamworks.NET is being run in this platform.", this);
	// 		}
	// 		if (!DllCheck.Test())
	// 		{
	// 			Debug.LogError("[Steamworks.NET] DllCheck Test returned false, One or more of the Steamworks binaries seems to be the wrong version.", this);
	// 		}
	// 		try
	// 		{
	// 			if (SteamAPI.RestartAppIfNecessary(new AppId_t(3372060u)))
	// 			{
	// 				Debug.Log("[Steamworks.NET] Shutting down because RestartAppIfNecessary returned true. Steam will restart the application.");
	// 				Application.Quit();
	// 				return;
	// 			}
	// 		}
	// 		catch (DllNotFoundException ex)
	// 		{
	// 			Debug.LogError("[Steamworks.NET] Could not load [lib]steam_api.dll/so/dylib. It's likely not in the correct location. Refer to the README for more details.\n" + ex, this);
	// 			Application.Quit();
	// 			return;
	// 		}
	// 		_bInitialized = SteamAPI.Init();
	// 		if (!_bInitialized)
	// 		{
	// 			Debug.LogError("[Steamworks.NET] SteamAPI_Init() failed. Refer to Valve's documentation or the comment above this line for more information.", this);
	// 			return;
	// 		}
	// 		s_EverInitialized = true;
	// 		Debug.Log("Steam API initialized");
	// 	}
	//
	// 	protected virtual void OnEnable()
	// 	{
	// 		if (s_instance == null)
	// 		{
	// 			s_instance = this;
	// 		}
	// 		if (_bInitialized)
	// 		{
	// 			if (m_SteamAPIWarningMessageHook == null)
	// 			{
	// 				m_SteamAPIWarningMessageHook = SteamAPIDebugTextHook;
	// 				SteamClient.SetWarningMessageHook(m_SteamAPIWarningMessageHook);
	// 			}
	// 			if (_bInitialized)
	// 			{
	// 				_GameOverlayActivated = Callback<GameOverlayActivated_t>.Create(OnGameOverlayActivated);
	// 			}
	// 			OnUserStatsReceivedCallResult = CallResult<UserStatsReceived_t>.Create(OnUserStatsReceived);
	// 			m_UserStatsReceived = Callback<UserStatsReceived_t>.Create(OnUserStatsReceived);
	// 			RequestCurrentStats();
	// 		}
	// 	}
	//
	// 	private void OnGameOverlayActivated(GameOverlayActivated_t pCallback)
	// 	{
	// 		if (pCallback.m_bActive != 0)
	// 		{
	// 			_OverlayOpened = true;
	// 		}
	// 		else
	// 		{
	// 			_OverlayOpened = false;
	// 		}
	// 	}
	//
	// 	public static string GetPersonaName()
	// 	{
	// 		if (Initialized)
	// 		{
	// 			return SteamFriends.GetPersonaName();
	// 		}
	// 		return "[Steamworks.NET] Invalid: Cannot Retrieve Persona Name";
	// 	}
	//
	// 	public static bool GetAchievement(string pchName, out bool pbAchieved)
	// 	{
	// 		bool achievement = SteamUserStats.GetAchievement(pchName, out pbAchieved);
	// 		if (pbAchieved)
	// 		{
	// 			Debug.LogWarning(achievement ? ("[Steamworks.NET] Achievement '" + pchName + "' retrieved successfully and already unlocked!") : ("[Steamworks.NET] Failed retrieving '" + pchName + "' achievement!"));
	// 		}
	// 		else
	// 		{
	// 			Debug.LogWarning(achievement ? ("[Steamworks.NET] Achievement '" + pchName + "' retrieved successfully and not unlocked!") : ("[Steamworks.NET] Failed retrieving '" + pchName + "' achievement!"));
	// 		}
	// 		return achievement;
	// 	}
	//
	// 	public static bool SetAchievement(string pchName)
	// 	{
	// 		throw new NotImplementedException();
	// 	}
	//
	// 	public static bool GetStat(string pchName, out int pData)
	// 	{
	// 		bool stat = SteamUserStats.GetStat(pchName, out pData);
	// 		Debug.LogWarning(stat ? ("[Steamworks.NET] Stat '" + pchName + "' with value '" + pData + "' get successfully!") : ("[Steamworks.NET] Failed getting '" + pchName + "' stat!"));
	// 		return stat;
	// 	}
	//
	// 	public static bool SetStat(string pchName, int nData)
	// 	{
	// 		bool flag = SteamUserStats.SetStat(pchName, nData);
	// 		Debug.LogWarning(flag ? ("[Steamworks.NET] Stat '" + pchName + "' with value '" + nData + "' set successfully!") : ("[Steamworks.NET] Failed setting '" + pchName + "' stat!"));
	// 		return flag;
	// 	}
	//
	// 	public static bool GetStat(string pchName, out float pData)
	// 	{
	// 		throw new NotImplementedException();
	// 	}
	//
	// 	public static bool SetStat(string pchName, float fData)
	// 	{
	// 		throw new NotImplementedException();
	// 	}
	//
	// 	public static bool RequestCurrentStats()
	// 	{
	// 		SteamAPICall_t steamAPICall_t = SteamUserStats.RequestUserStats(GameServer.GetSteamID());
	// 		OnUserStatsReceivedCallResult.Set(steamAPICall_t);
	// 		string text = GameServer.GetSteamID().ToString();
	// 		SteamAPICall_t steamAPICall_t2 = steamAPICall_t;
	// 		MonoBehaviour.print("SteamUserStats.RequestUserStats(" + text + ") : " + steamAPICall_t2.ToString());
	// 		return true;
	// 	}
	//
	// 	private void OnUserStatsReceived(UserStatsReceived_t pCallback)
	// 	{
	// 		string[] obj = new string[8]
	// 		{
	// 			"[",
	// 			1101.ToString(),
	// 			" - UserStatsReceived] - ",
	// 			pCallback.m_nGameID.ToString(),
	// 			" -- ",
	// 			pCallback.m_eResult.ToString(),
	// 			" -- ",
	// 			null
	// 		};
	// 		CSteamID steamIDUser = pCallback.m_steamIDUser;
	// 		obj[7] = steamIDUser.ToString();
	// 		Debug.Log(string.Concat(obj));
	// 	}
	//
	// 	private void OnUserStatsReceived(UserStatsReceived_t pCallback, bool bIOFailure)
	// 	{
	// 		string[] obj = new string[8]
	// 		{
	// 			"[",
	// 			1101.ToString(),
	// 			" - UserStatsReceived] - ",
	// 			pCallback.m_nGameID.ToString(),
	// 			" -- ",
	// 			pCallback.m_eResult.ToString(),
	// 			" -- ",
	// 			null
	// 		};
	// 		CSteamID steamIDUser = pCallback.m_steamIDUser;
	// 		obj[7] = steamIDUser.ToString();
	// 		Debug.Log(string.Concat(obj));
	// 	}
	//
	// 	public static bool StoreStats()
	// 	{
	// 		bool num = SteamUserStats.StoreStats();
	// 		Debug.LogWarning(num ? "[Steamworks.NET] Stats stored successfully!" : "[Steamworks.NET] Failed storing stats!");
	// 		return num;
	// 	}
	//
	// 	protected virtual void OnDestroy()
	// 	{
	// 		if (!(s_instance != this))
	// 		{
	// 			s_instance = null;
	// 			if (_bInitialized)
	// 			{
	// 				SteamAPI.Shutdown();
	// 			}
	// 		}
	// 	}
	//
	// 	private void Update()
	// 	{
	// 		if (_bInitialized)
	// 		{
	// 			SteamAPI.RunCallbacks();
	// 		}
	// 	}
	//
	// 	private IEnumerator UpdateCallbacks()
	// 	{
	// 		while (true)
	// 		{
	// 			if (!_bInitialized)
	// 			{
	// 				yield return null;
	// 			}
	// 			SteamAPI.RunCallbacks();
	// 			yield return null;
	// 		}
	// 	}
	//
	// 	public void ForceUpdateCallbacks()
	// 	{
	// 		if (_bInitialized)
	// 		{
	// 			SteamAPI.RunCallbacks();
	// 		}
	// 	}
	}
}
