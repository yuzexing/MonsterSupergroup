#if UNITY_EDITOR || MONSTER_MENU_VALIDATION
using System.Collections;
using Mirror;
using MonsterSupergroup.NetworkCombat;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class SteamInviteUiTests
    {
        private UnityEngine.GameObject[] roots;
        private BootGameplayNetworkManager manager;
        [UnityTest] public IEnumerator FriendPicker_SearchScrollKeyboardRefreshSendAndCleanup_WithoutSteamOverlay()
        {
            const string path = "Assets/_Project/Scenes/Boot.unity";
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
#endif
            roots = BootSceneFixtureObjects.Capture(path);
            manager = NetworkManager.singleton as BootGameplayNetworkManager;
            manager.ConfigurePreparationFlow(true);
            yield return manager.EnsureMainMenu();
            yield return SteamInviteUiScenario.Run(manager);
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if (manager != null)
            {
                manager.LeavePreparationRoom();
                float deadline = UnityEngine.Time.realtimeSinceStartup + 30;
                while ((NetworkServer.active || NetworkClient.active || manager.IsLeavingRoom) && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            }
            BootSceneFixtureObjects.Destroy(roots); yield return null; NetworkManager.ResetStatics();
        }
    }
}
#endif
