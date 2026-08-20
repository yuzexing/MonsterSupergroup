using System;
using System.Threading;
using AstralShift.DebugTools;
using AstralShift.HellMaiden.Audio;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.DevDebug;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.ProfileData;
using AstralShift.HellMaiden.Scenes;
using AstralShift.HellMaiden.UI;
using AstralShift.HellMaiden.UI.Cards;
using AstralShift.Helpers.Steam;
using AstralShift.Managers;
using AstralShift.ProfileData;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace AstralShift.HellMaiden
{
	public class GameDirector : MonoBehaviour
	{
		public static GameDirector Instance;

		public SceneMaster sceneMaster;
		
		public ControllerManager controllerManager;
		
		public GameDataManager gameDataManager;
		
		public TutorialManager tutorialManager;

		public RuntimeDB runtimeDB;
		//
		public MusicPlayer musicPlayer;
		
		public IntercomManager intercomManager;
		
		public PointerManager pointerManager;
		
		public UICardRenderingManager cardRenderingManager;
		
		public TestManager testManager;

		[SerializeField]
		private PlayerMovement player;

		[Header("DevMod")]
		[SerializeField]
		private DevMod buildDevMod;

		[HideInInspector]
		public bool QuittingMenu;

		[SerializeField]
		private int fpsLimit = 1000;

		private CancellationTokenSource _destroyCts;

		private bool consoleHidden;

		public SettingsManager Settings { get; set; }
		
		public AchievementManager AchievementManager { get; set; }
		
		public PlatformToolKitWrapper PlatformToolKitWrapper { get; set; }

		public PlayerMovement Player => player;

		private void Awake()
		{
			Instance = this;
			_destroyCts = new CancellationTokenSource();
			// RunInitializationSequence(_destroyCts.Token).Forget();
		}

		private void OnDestroy()
		{
			_destroyCts?.Cancel();
			_destroyCts?.Dispose();
		}

		private async UniTaskVoid RunInitializationSequence(CancellationToken token)
		{
			DBL.LogHardwareInfo();
			await InitCore(token);
			DeveloperDebug.buildDevMod = buildDevMod;
			await RunDataInitializationStep(token);
			await InitManagers(token);
			sceneMaster.LoadFirstScene();
			PlayerHand.Instance.Init();
		}
		
		private async UniTask InitCore(CancellationToken token)
		{
			try
			{
				sceneMaster.Init();
				controllerManager.Init();
				gameDataManager.Init();
				runtimeDB.Init();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}
		
		private async UniTask InitManagers(CancellationToken token)
		{
			try
			{
				cardRenderingManager.Init();
				tutorialManager.Init();
				musicPlayer.Init();
				intercomManager.Init();
				pointerManager.Init();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}
		
		private async UniTask RunDataInitializationStep(CancellationToken token)
		{
			_ = 2;
			try
			{
				await Addressables.InitializeAsync().ToUniTask(null, PlayerLoopTiming.Update, token);
				PlatformToolKitWrapper = new PlatformToolKitWrapper();
				AchievementManager = new AchievementManager();
				ProfileDataManager.Adapter = new SteamProfileData();
				base.gameObject.AddComponent<SteamManager>();
				ProfileDataManager.LoadConfigs();
				Settings = GetComponentInChildren<SettingsManager>();
				Settings.Setup();
				Settings.LoadSettings();
				if ((!DeveloperDebug.devMode || DeveloperDebug.LoadEditorSave) && SaveManager.HasSaveFiles())
				{
					await SaveManager.LoadGameFromSaveSlotAsync(0).AsUniTask().AttachExternalCancellation(token);
				}
				await UniTask.NextFrame(token);
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private void Update()
		{
		}
	}
}
